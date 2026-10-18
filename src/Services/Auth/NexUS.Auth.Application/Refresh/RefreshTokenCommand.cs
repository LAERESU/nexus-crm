using MediatR;
using Microsoft.Extensions.Logging;
using NexUS.Auth.Application.Abstractions;
using NexUS.Auth.Application.Common;
using NexUS.Auth.Application.Login;
using NexUS.Auth.Domain.Credentials;
using NexUS.BuildingBlocks.Common.Exceptions;

namespace NexUS.Auth.Application.Refresh;

public sealed record RefreshTokenCommand(string? RefreshToken, string? IpAddress, string? UserAgent) : IRequest<LoginResult>;

/// <summary>
/// Rotasi refresh token. Token yang sudah dicabut lalu dipakai lagi dianggap dicuri: seluruh keluarga sesi
/// dicabut sehingga pemegang token baru (penyerang atau korban) harus login ulang.
/// </summary>
public sealed class RefreshTokenCommandHandler(
    IRefreshTokenRepository refreshTokens,
    ICredentialRepository credentials,
    IAccessTokenIssuer accessTokenIssuer,
    IUserDirectory userDirectory,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<RefreshTokenCommandHandler> logger) : IRequestHandler<RefreshTokenCommand, LoginResult>
{
    public const string GenericFailureMessage = "Sesi tidak valid. Silakan login ulang.";

    public async Task<LoginResult> Handle(RefreshTokenCommand command, CancellationToken ct)
    {
        if (!RefreshTokenSecret.TryGetTenantId(command.RefreshToken, out var tenantId))
            throw Failure("format token tidak valid");

        var now = timeProvider.GetUtcNow();
        var current = await refreshTokens.FindByHashAsync(tenantId, RefreshTokenSecret.Hash(command.RefreshToken!), ct)
                      ?? throw Failure("token tidak dikenal");

        if (current.IsRevoked)
        {
            await RevokeFamilyAsync(current, now, ct);
            throw Failure("token yang sudah dicabut dipakai lagi; keluarga sesi dicabut");
        }

        if (!current.IsActive(now)) throw Failure("token kedaluwarsa");

        var credential = await credentials.FindByIdAsync(tenantId, current.CredentialId, ct);
        if (credential is null || credential.IsLocked(now)) throw Failure("kredensial tidak ada atau terkunci");

        var nextValue = RefreshTokenSecret.Generate(tenantId);
        var next = current.Rotate(RefreshTokenSecret.Hash(nextValue), now, command.UserAgent, command.IpAddress);
        await refreshTokens.AddAsync(next, ct);

        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (ConcurrencyException)
        {
            // Dua refresh bersamaan dengan token yang sama: yang kalah ditolak tanpa mencabut keluarga,
            // agar dua tab milik pengguna yang sama tidak saling mengeluarkan. Klien wajib refresh satu per satu.
            throw Failure("refresh bersamaan dengan token yang sama");
        }

        var roles = await userDirectory.GetRolesAsync(credential, ct);
        var accessToken = accessTokenIssuer.Issue(credential, roles);
        logger.LogInformation("Refresh token dirotasi untuk user {UserId} di tenant {TenantId}", credential.UserId, tenantId);
        return new LoginResult(accessToken.Value, accessToken.ExpiresAt, nextValue, next.ExpiresAt);
    }

    private async Task RevokeFamilyAsync(RefreshToken reused, DateTimeOffset now, CancellationToken ct)
    {
        var revoked = await refreshTokens.RevokeFamilyAsync(
            reused.TenantId, reused.FamilyId, now, RefreshTokenRevocationReason.ReuseDetected, ct);
        logger.LogWarning("Pemakaian ulang refresh token terdeteksi; {Count} token keluarga {FamilyId} dicabut", revoked, reused.FamilyId);
    }

    private UnauthorizedException Failure(string reason)
    {
        logger.LogInformation("Refresh token ditolak: {Reason}", reason);
        return new UnauthorizedException(GenericFailureMessage);
    }
}
