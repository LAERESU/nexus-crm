using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;

namespace NexUS.Auth.Domain.Credentials;

/// <summary>Refresh token yang sudah diterbitkan. Hanya hash-nya yang disimpan; token asli dipegang klien.</summary>
public sealed class RefreshToken : Entity
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public long CredentialId { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public long? ReplacedById { get; private set; }
    public string? DeviceInfo { get; private set; }
    public string? CreatedIp { get; private set; }

    private RefreshToken() { } // untuk EF Core

    public static RefreshToken Issue(Credential credential, string tokenHash, DateTimeOffset now, string? deviceInfo, string? ip)
    {
        if (string.IsNullOrWhiteSpace(tokenHash)) throw new DomainException("Hash token wajib diisi.");

        return new RefreshToken
        {
            TenantId = credential.TenantId,
            CredentialId = credential.Id,
            TokenHash = tokenHash,
            ExpiresAt = now + Lifetime,
            DeviceInfo = Truncate(deviceInfo, 500),
            CreatedIp = Truncate(ip, 45),
        };
    }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
