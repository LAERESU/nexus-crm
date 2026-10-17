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

    public async Task<LoginResult> Handle(LoginCommand command, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var credential = await credentials.FindByEmailAsync(Credential.NormalizeEmail(command.Email), ct);

        if (credential is null)
        {
            passwordHasher.VerifyDummy(command.Password);
            await FailAsync(null, LoginFailureReason.UnknownEmail, command, now, ct);
        }
        else if (credential.IsLocked(now))
        {
            await FailAsync(credential, LoginFailureReason.Locked, command, now, ct);
        }
        else if (!passwordHasher.Verify(command.Password, credential.PasswordHash))
        {
            credential.RegisterFailedLogin(now);
            await FailAsync(credential, LoginFailureReason.WrongPassword, command, now, ct);
        }

        credential!.RegisterSuccessfulLogin(now);
        var roles = await userDirectory.GetRolesAsync(credential, ct);
        var accessToken = accessTokenIssuer.Issue(credential, roles);

        var refreshTokenValue = RefreshTokenSecret.Generate();
        var refreshToken = RefreshToken.Issue(credential, RefreshTokenSecret.Hash(refreshTokenValue), now, command.UserAgent, command.IpAddress);

        await refreshTokens.AddAsync(refreshToken, ct);
        await loginAttempts.AddAsync(LoginAttempt.Success(credential, now, command.IpAddress, command.UserAgent), ct);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("Login berhasil untuk user {UserId} di tenant {TenantId}", credential.UserId, credential.TenantId);
        return new LoginResult(accessToken.Value, accessToken.ExpiresAt, refreshTokenValue, refreshToken.ExpiresAt);
    }

    private async Task FailAsync(
        Credential? credential, LoginFailureReason reason, LoginCommand command, DateTimeOffset now, CancellationToken ct)
    {
        await loginAttempts.AddAsync(LoginAttempt.Failure(credential, reason, now, command.IpAddress, command.UserAgent), ct);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogWarning("Login gagal ({Reason}) untuk user {UserId}", reason, credential?.UserId);
        throw new UnauthorizedException(GenericFailureMessage);
    }
}
