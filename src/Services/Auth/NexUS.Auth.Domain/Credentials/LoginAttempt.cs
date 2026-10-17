using NexUS.BuildingBlocks.Common.Domain;

namespace NexUS.Auth.Domain.Credentials;

public enum LoginFailureReason
{
    UnknownEmail,
    WrongPassword,
    Locked,
}

/// <summary>Satu baris LOGIN_HISTORY. Email yang dicoba sengaja tidak disimpan (data pribadi).</summary>
public sealed class LoginAttempt : Entity
{
    public long? CredentialId { get; private set; }
    public bool Succeeded { get; private set; }
    public LoginFailureReason? FailureReason { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    private LoginAttempt() { } // untuk EF Core

    public static LoginAttempt Success(Credential credential, DateTimeOffset now, string? ip, string? userAgent) =>
        Create(credential, succeeded: true, reason: null, now, ip, userAgent);

    public static LoginAttempt Failure(Credential? credential, LoginFailureReason reason, DateTimeOffset now, string? ip, string? userAgent) =>
        Create(credential, succeeded: false, reason, now, ip, userAgent);

    private static LoginAttempt Create(
        Credential? credential, bool succeeded, LoginFailureReason? reason, DateTimeOffset now, string? ip, string? userAgent) =>
        new()
        {
            TenantId = credential?.TenantId ?? 0,
            CredentialId = credential?.Id,
            Succeeded = succeeded,
            FailureReason = reason,
            IpAddress = ip is { Length: > 45 } ? ip[..45] : ip,
            UserAgent = userAgent is { Length: > 500 } ? userAgent[..500] : userAgent,
            OccurredAt = now,
        };
}
