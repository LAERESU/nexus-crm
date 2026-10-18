using MediatR;
using Microsoft.Extensions.Logging;
using NexUS.Auth.Application.Abstractions;
using NexUS.Auth.Application.Common;
using NexUS.Auth.Domain.Credentials;

namespace NexUS.Auth.Application.Logout;

/// <summary>
/// Mencabut keluarga refresh token sesi ini (semua token hasil rotasinya) dan memasukkan access token ke blacklist.
/// Idempoten dan tidak pernah gagal karena token tidak valid: tujuan logout adalah mengakhiri sesi, bukan memverifikasinya.
/// </summary>
public sealed class LogoutCommandHandler(
    IRefreshTokenRepository refreshTokens,
    ICredentialRepository credentials,
    IAccessTokenReader accessTokenReader,
    IAccessTokenBlacklist blacklist,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<LogoutCommandHandler> logger) : IRequestHandler<LogoutCommand>
{
    public async Task Handle(LogoutCommand command, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();

        var accessToken = await accessTokenReader.ReadAsync(command.AccessToken);
        if (accessToken is not null) await blacklist.RevokeAsync(accessToken, now, ct);

        if (!RefreshTokenSecret.TryGetTenantId(command.RefreshToken, out var tenantId)) return;

        var refreshToken = await refreshTokens.FindByHashAsync(tenantId, RefreshTokenSecret.Hash(command.RefreshToken!), ct);
        if (refreshToken is null) return;

        var revoked = await refreshTokens.RevokeFamilyAsync(
            tenantId, refreshToken.FamilyId, now, RefreshTokenRevocationReason.Logout, ct);

        var credential = await credentials.FindByIdAsync(tenantId, refreshToken.CredentialId, ct);
        if (credential is not null)
        {
            credential.RecordLogout(refreshToken.FamilyId, revoked);
            await unitOfWork.SaveChangesAsync(ct);
        }

        logger.LogInformation("Logout user {UserId} di tenant {TenantId}; {Count} refresh token dicabut",
            credential?.UserId, tenantId, revoked);
    }
}
