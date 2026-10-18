using NexUS.BuildingBlocks.Caching;

namespace NexUS.BuildingBlocks.Auth;

/// <summary>
/// Blacklist <c>jti</c> access token yang sudah di-logout. Ditulis Auth Service, dibaca Gateway dan service
/// saat memvalidasi JWT (NX-015).
/// </summary>
public interface ITokenRevocationStore
{
    /// <summary>Menandai token dicabut sampai waktu kedaluwarsanya; token yang sudah kedaluwarsa diabaikan.</summary>
    Task RevokeAsync(long tenantId, string jti, DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken ct = default);

    Task<bool> IsRevokedAsync(long tenantId, string jti, CancellationToken ct = default);
}

/// <summary>
/// Blacklist di Redis dengan TTL sisa umur token, berawalan <c>tnt:{tenantId}:</c> (CONTEXT.md bagian 3 poin 8).
/// Redis hanya cache: bila Redis mati, token dianggap tidak dicabut (fail open). Risikonya terbatas pada sisa
/// umur access token (15 menit), sedangkan refresh token tetap dicabut di Oracle.
/// </summary>
public sealed class CacheTokenRevocationStore(ICacheService cache) : ITokenRevocationStore
{
    private const string RevokedMarker = "1";

    public static string Key(long tenantId, string jti) => CacheKeys.Tenant(tenantId, $"auth:revoked:{jti}");

    public Task RevokeAsync(long tenantId, string jti, DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken ct = default)
    {
        var remaining = expiresAt - now;
        return remaining <= TimeSpan.Zero || string.IsNullOrWhiteSpace(jti)
            ? Task.CompletedTask
            : cache.SetAsync(Key(tenantId, jti), RevokedMarker, remaining, ct);
    }

    public async Task<bool> IsRevokedAsync(long tenantId, string jti, CancellationToken ct = default) =>
        !string.IsNullOrWhiteSpace(jti) && await cache.GetAsync<string>(Key(tenantId, jti), ct) == RevokedMarker;
}
