using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;

namespace NexUS.Auth.Domain.Credentials;

/// <summary>Kredensial login satu user. Aturan lockout ada di sini, bukan di handler.</summary>
public sealed class Credential : AggregateRoot
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    public Guid UserId { get; private set; }
    public string Email { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public int FailedAttempts { get; private set; }
    public DateTimeOffset? FirstFailedAt { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }

    private Credential() { } // untuk EF Core

    public static Credential Create(long tenantId, Guid userId, string email, string passwordHash)
    {
        if (tenantId <= 0) throw new DomainException("Tenant wajib diisi.");
        if (userId == Guid.Empty) throw new DomainException("User wajib diisi.");
        if (string.IsNullOrWhiteSpace(passwordHash)) throw new DomainException("Hash password wajib diisi.");

        return new Credential
        {
            TenantId = tenantId,
            UserId = userId,
            Email = NormalizeEmail(email),
            PasswordHash = passwordHash,
        };
    }

    public static string NormalizeEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) throw new DomainException("Email wajib diisi.");
        return email.Trim().ToLowerInvariant();
    }

    public bool IsLocked(DateTimeOffset now) => LockedUntil is { } until && until > now;

    /// <summary>
    /// Mencatat password salah. Percobaan ke-<see cref="MaxFailedAttempts"/> dalam <see cref="FailureWindow"/>
    /// mengunci akun selama <see cref="LockDuration"/>.
    /// </summary>
    public void RegisterFailedLogin(DateTimeOffset now)
    {
        if (FirstFailedAt is null || now - FirstFailedAt > FailureWindow)
        {
            FailedAttempts = 0;
            FirstFailedAt = now;
        }

        FailedAttempts++;
        if (FailedAttempts < MaxFailedAttempts) return;

        LockedUntil = now + LockDuration;
        FailedAttempts = 0;
        FirstFailedAt = null;
        AddDomainEvent(new CredentialLockedDomainEvent(PublicId, TenantId, UserId, LockedUntil.Value));
    }

    /// <summary>
    /// Mencatat pencabutan sebuah sesi (logout atau indikasi pencurian refresh token) lewat outbox untuk audit.
    /// Nama event mengikuti kontrak <c>TokenRevoked</c> di ARCHITECTURE.md B.0.
    /// </summary>
    public void RecordSessionRevoked(Guid sessionId, RefreshTokenRevocationReason reason, int revokedTokens, string? ipAddress) =>
        AddDomainEvent(new TokenRevokedDomainEvent(PublicId, TenantId, UserId, sessionId, reason, revokedTokens, ipAddress));

    public void RegisterSuccessfulLogin(DateTimeOffset now)
    {
        if (IsLocked(now)) throw new DomainException("Akun sedang terkunci.");

        FailedAttempts = 0;
        FirstFailedAt = null;
        LockedUntil = null;
        AddDomainEvent(new UserLoggedInDomainEvent(PublicId, TenantId, UserId));
    }
}

public sealed record TokenRevokedDomainEvent(
    Guid CredentialId, long TenantId, Guid UserId, Guid SessionId, RefreshTokenRevocationReason Reason, int RevokedTokens, string? IpAddress)
    : DomainEvent;

public sealed record UserLoggedInDomainEvent(Guid CredentialId, long TenantId, Guid UserId) : DomainEvent;

public sealed record CredentialLockedDomainEvent(Guid CredentialId, long TenantId, Guid UserId, DateTimeOffset LockedUntil) : DomainEvent;
