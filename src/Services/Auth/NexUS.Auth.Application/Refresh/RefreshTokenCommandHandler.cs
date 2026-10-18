using MediatR;
using Microsoft.Extensions.Logging;
using NexUS.Auth.Application.Abstractions;
using NexUS.Auth.Application.Common;
using NexUS.Auth.Application.Login;
using NexUS.Auth.Domain.Credentials;
using NexUS.BuildingBlocks.Common.Exceptions;

namespace NexUS.Auth.Application.Refresh;

/// <summary>
/// Rotasi refresh token. Token yang sudah dicabut lalu dipakai lagi dianggap dicuri: seluruh keluarga sesi
/// dicabut sehingga pemegang token baru (penyerang atau korban) harus login ulang. Pengecualiannya token yang
/// baru saja dirotasi (<see cref="RefreshToken.ReuseGracePeriod"/>), yang dianggap refresh bersamaan dari tab lain.
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
            throw Reject("format token tidak valid", clearCookie: true);

        var now = timeProvider.GetUtcNow();
        var current = await refreshTokens.FindByHashAsync(tenantId, RefreshTokenSecret.Hash(command.RefreshToken!), ct)
                      ?? throw Reject("token tidak dikenal", clearCookie: true);

        if (current.IsRevoked)
        {
            if (current.WasJustRotated(now))
                throw Reject("token baru saja dirotasi oleh request lain (refresh bersamaan)", clearCookie: false);

            await RevokeFamilyAsync(current, command.IpAddress, now, ct);
            throw Reject("token yang sudah dicabut dipakai lagi; keluarga sesi dicabut", clearCookie: true);
        }

        if (!current.IsActive(now)) throw Reject("token kedaluwarsa", clearCookie: true);

        // Kredensial terkunci tidak menolak refresh: lockout mencegah tebak password, dan bisa dipicu siapa pun,
        // jadi tidak boleh dipakai untuk mengeluarkan sesi aktif korban.
        var credential = await credentials.FindByIdAsync(tenantId, current.CredentialId, ct)
                         ?? throw Reject("kredensial tidak ada", clearCookie: true);

        // Siapkan semua hasil sebelum menyimpan, agar rotasi tidak ter-commit tanpa token baru sampai ke klien.
        var roles = await userDirectory.GetRolesAsync(credential, ct);
        var accessToken = accessTokenIssuer.Issue(credential, roles);
        var nextValue = RefreshTokenSecret.Generate(tenantId);
        var next = current.Rotate(RefreshTokenSecret.Hash(nextValue), now, command.UserAgent, command.IpAddress);
        await refreshTokens.AddAsync(next, ct);

        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (ConcurrencyException)
        {
            throw Reject("refresh bersamaan dengan token yang sama", clearCookie: false);
        }

        logger.LogInformation("Refresh token dirotasi untuk user {UserId} di tenant {TenantId}", credential.UserId, tenantId);
        return new LoginResult(accessToken.Value, accessToken.ExpiresAt, nextValue, next.ExpiresAt);
    }

    private async Task RevokeFamilyAsync(RefreshToken reused, string? ipAddress, DateTimeOffset now, CancellationToken ct)
    {
        var revoked = await refreshTokens.RevokeFamilyAsync(
            reused.TenantId, reused.FamilyId, now, RefreshTokenRevocationReason.ReuseDetected, ct);

        // Dicatat lewat outbox agar tersimpan untuk audit (CONTEXT.md bagian 8), bukan hanya di log.
        var credential = await credentials.FindByIdAsync(reused.TenantId, reused.CredentialId, ct);
        if (credential is not null)
        {
            credential.RecordRefreshTokenReuse(reused.FamilyId, revoked, ipAddress);
            await unitOfWork.SaveChangesAsync(ct);
        }

        logger.LogWarning(
            "Pemakaian ulang refresh token terdeteksi untuk user {UserId} di tenant {TenantId}; {Count} token keluarga {FamilyId} dicabut",
            credential?.UserId, reused.TenantId, revoked, reused.FamilyId);
    }

    private RefreshRejectedException Reject(string reason, bool clearCookie)
    {
        logger.LogInformation("Refresh token ditolak: {Reason}", reason);
        return new RefreshRejectedException(GenericFailureMessage, clearCookie);
    }
}
