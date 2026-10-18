using MediatR;
using Microsoft.Extensions.Logging;
using NexUS.Auth.Application.Abstractions;
using NexUS.Auth.Application.Common;
using NexUS.Auth.Domain.Credentials;
using NexUS.BuildingBlocks.Common.Exceptions;

namespace NexUS.Auth.Application.Login;

public sealed class LoginCommandHandler(
    ICredentialRepository credentials,
    IRefreshTokenRepository refreshTokens,
    ILoginAttemptRepository loginAttempts,
    IPasswordHasher passwordHasher,
    IAccessTokenIssuer accessTokenIssuer,
    IUserDirectory userDirectory,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<LoginCommandHandler> logger) : IRequestHandler<LoginCommand, LoginResult>
{
    /// <summary>Satu pesan untuk semua kegagalan agar tidak membocorkan apakah email terdaftar atau akun terkunci.</summary>
    public const string GenericFailureMessage = "Email atau password salah.";

    /// <summary>Percobaan ulang bila baris kredensial diubah request lain secara bersamaan (ROW_VERSION).</summary>
    public const int MaxConcurrencyRetries = 3;

    public async Task<LoginResult> Handle(LoginCommand command, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var credential = await credentials.FindByEmailAsync(Credential.NormalizeEmail(command.Email), ct);

        if (credential is null)
        {
            passwordHasher.VerifyDummy(command.Password); // biaya sama dengan verifikasi asli
            await RecordFailureAsync(null, LoginFailureReason.UserNotFound, command, now, ct);
            await unitOfWork.SaveChangesAsync(ct);
            throw Failure(null, LoginFailureReason.UserNotFound);
        }

        // Selalu verifikasi sebelum cek kunci agar akun terkunci tidak merespons lebih cepat (timing leak).
        var passwordValid = passwordHasher.Verify(command.Password, credential.PasswordHash);

        if (credential.IsLocked(now))
        {
            await RecordFailureAsync(credential, LoginFailureReason.Locked, command, now, ct);
            await unitOfWork.SaveChangesAsync(ct);
            throw Failure(credential, LoginFailureReason.Locked);
        }

        if (!passwordValid)
        {
            await RecordFailureAsync(credential, LoginFailureReason.InvalidLogin, command, now, ct);
            // Dihitung juga saat ada request paralel: konflik ROW_VERSION → muat ulang → hitung lagi.
            await SaveWithRetryAsync(credential, c => c.RegisterFailedLogin(now), ct);
            throw Failure(credential, LoginFailureReason.InvalidLogin);
        }

        var roles = await userDirectory.GetRolesAsync(credential, ct);
        var refreshTokenValue = RefreshTokenSecret.Generate(credential.TenantId);
        var refreshToken = RefreshToken.Issue(credential, RefreshTokenSecret.Hash(refreshTokenValue), now, command.UserAgent, command.IpAddress);
        await refreshTokens.AddAsync(refreshToken, ct);
        await loginAttempts.AddAsync(LoginAttempt.Success(credential, now, command.IpAddress, command.UserAgent), ct);

        await SaveWithRetryAsync(credential, c =>
        {
            // Bisa terkunci oleh request lain di antara verifikasi dan penyimpanan.
            if (c.IsLocked(now)) throw Failure(c, LoginFailureReason.Locked);
            c.RegisterSuccessfulLogin(now);
        }, ct);

        var accessToken = accessTokenIssuer.Issue(credential, roles);
        logger.LogInformation("Login berhasil untuk user {UserId} di tenant {TenantId}", credential.UserId, credential.TenantId);
        return new LoginResult(accessToken.Value, accessToken.ExpiresAt, refreshTokenValue, refreshToken.ExpiresAt);
    }

    private async Task SaveWithRetryAsync(Credential credential, Action<Credential> apply, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            apply(credential);
            try
            {
                await unitOfWork.SaveChangesAsync(ct);
                return;
            }
            catch (ConcurrencyException) when (attempt < MaxConcurrencyRetries)
            {
                logger.LogInformation("Kredensial {CredentialId} diubah bersamaan; mengulang ({Attempt})", credential.PublicId, attempt);
                await credentials.ReloadAsync(credential, ct);
            }
        }
    }

    private Task RecordFailureAsync(
        Credential? credential, LoginFailureReason reason, LoginCommand command, DateTimeOffset now, CancellationToken ct) =>
        loginAttempts.AddAsync(LoginAttempt.Failure(credential, reason, now, command.IpAddress, command.UserAgent), ct);

    private UnauthorizedException Failure(Credential? credential, LoginFailureReason reason)
    {
        logger.LogWarning("Login gagal ({Reason}) untuk user {UserId}", reason, credential?.UserId);
        return new UnauthorizedException(GenericFailureMessage);
    }
}
