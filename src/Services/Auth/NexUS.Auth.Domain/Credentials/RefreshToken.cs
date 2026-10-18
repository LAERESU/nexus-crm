using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;

namespace NexUS.Auth.Domain.Credentials;

public enum RefreshTokenRevocationReason
{
    Rotated,
    ReuseDetected,
    Logout,
}

/// <summary>
/// Refresh token yang sudah diterbitkan. Hanya hash-nya yang disimpan; token asli dipegang klien.
/// Setiap pemakaian merotasi token (token lama dicabut, token baru satu keluarga diterbitkan).
/// </summary>
public sealed class RefreshToken : Entity
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public long CredentialId { get; private set; }
    public Guid FamilyId { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public RefreshTokenRevocationReason? RevokedReason { get; private set; }
    public long? ReplacedById { get; private set; }
    public RefreshToken? ReplacedBy { get; private set; }
    public string? DeviceInfo { get; private set; }
    public string? CreatedIp { get; private set; }

    private RefreshToken() { } // untuk EF Core

    /// <summary>Token pertama sebuah sesi (saat login) memulai keluarga baru.</summary>
    public static RefreshToken Issue(Credential credential, string tokenHash, DateTimeOffset now, string? deviceInfo, string? ip) =>
        Create(credential.TenantId, credential.Id, Guid.NewGuid(), tokenHash, now, deviceInfo, ip);

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    /// <summary>Token yang sudah dicabut tetapi dipakai lagi adalah tanda token dicuri.</summary>
    public bool IsRevoked => RevokedAt is not null;

    /// <summary>Mencabut token ini dan menerbitkan penggantinya dalam keluarga yang sama.</summary>
    public RefreshToken Rotate(string newTokenHash, DateTimeOffset now, string? deviceInfo, string? ip)
    {
        if (!IsActive(now)) throw new DomainException("Refresh token tidak aktif.");

        var next = Create(TenantId, CredentialId, FamilyId, newTokenHash, now, deviceInfo, ip);
        Revoke(now, RefreshTokenRevocationReason.Rotated);
        ReplacedBy = next;
        return next;
    }

    /// <summary>Idempoten: token yang sudah dicabut tidak diubah lagi.</summary>
    public void Revoke(DateTimeOffset now, RefreshTokenRevocationReason reason)
    {
        if (RevokedAt is not null) return;
        RevokedAt = now;
        RevokedReason = reason;
    }

    private static RefreshToken Create(
        long tenantId, long credentialId, Guid familyId, string tokenHash, DateTimeOffset now, string? deviceInfo, string? ip)
    {
        if (string.IsNullOrWhiteSpace(tokenHash)) throw new DomainException("Hash token wajib diisi.");

        return new RefreshToken
        {
            TenantId = tenantId,
            CredentialId = credentialId,
            FamilyId = familyId,
            TokenHash = tokenHash,
            ExpiresAt = now + Lifetime,
            DeviceInfo = Truncate(deviceInfo, 500),
            CreatedIp = Truncate(ip, 45),
        };
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
