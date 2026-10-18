using Microsoft.Extensions.Logging;
using NexUS.BuildingBlocks.Caching;
using StackExchange.Redis;

namespace NexUS.BuildingBlocks.Auth;

/// <summary>Nilai bersama penerbit (Auth) dan validator (Gateway, service) JWT.</summary>
public static class NexusJwt
{
    public const string TenantClaim = "tid";
    public const string SessionClaim = "sid";

    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Toleransi jam antar server. Validator wajib memakai nilai yang sama (NX-015).</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Blacklist access token yang sudah di-logout: per token (<c>jti</c>) dan per sesi (<c>sid</c>, satu keluarga
/// refresh token). Ditulis Auth Service, dibaca Gateway dan service saat memvalidasi JWT (NX-015). Lihat ADR-0007.
/// </summary>
public interface ITokenRevocationStore
{
    /// <summary>Mencabut satu token sampai <c>exp + ClockSkew</c>; token di luar jendela itu diabaikan.</summary>
    Task RevokeTokenAsync(long tenantId, string jti, DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Mencabut semua access token sebuah sesi, termasuk yang diterbitkan sebelum refresh terakhir.</summary>
    Task RevokeSessionAsync(long tenantId, string sessionId, CancellationToken ct = default);

    Task<bool> IsRevokedAsync(long tenantId, string jti, string? sessionId, CancellationToken ct = default);
}

/// <summary>
/// Blacklist di Redis dengan key tetap berawalan <c>tnt:{tenantId}:</c> (tanpa <c>Cache:KeyPrefix</c> per service,
/// agar penulis dan pembaca selalu memakai key yang sama). Redis hanya cache: bila Redis tidak terjangkau, token
/// dianggap tidak dicabut (fail open) dan kejadiannya dicatat. Risikonya terbatas pada sisa umur access token.
/// </summary>
public sealed class RedisTokenRevocationStore(IRedisConnection redis, ILogger<RedisTokenRevocationStore> logger) : ITokenRevocationStore
{
    private static readonly RedisValue Marker = "1";

    public static string TokenKey(long tenantId, string jti) => CacheKeys.Tenant(tenantId, $"auth:revoked:{jti}");

    public static string SessionKey(long tenantId, string sessionId) => CacheKeys.Tenant(tenantId, $"auth:revoked-sid:{sessionId}");

    public Task RevokeTokenAsync(long tenantId, string jti, DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken ct = default)
    {
        var ttl = expiresAt + NexusJwt.ClockSkew - now;
        return ttl <= TimeSpan.Zero || string.IsNullOrWhiteSpace(jti)
            ? Task.CompletedTask
            : SetAsync(TokenKey(tenantId, jti), ttl);
    }

    public Task RevokeSessionAsync(long tenantId, string sessionId, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(sessionId)
            ? Task.CompletedTask
            // Token apa pun dari sesi ini paling lama berlaku AccessTokenLifetime + ClockSkew sejak diterbitkan.
            : SetAsync(SessionKey(tenantId, sessionId), NexusJwt.AccessTokenLifetime + NexusJwt.ClockSkew);

    public async Task<bool> IsRevokedAsync(long tenantId, string jti, string? sessionId, CancellationToken ct = default)
    {
        if (redis.Multiplexer is not { } multiplexer || string.IsNullOrWhiteSpace(jti)) return false;

        RedisKey[] keys = string.IsNullOrWhiteSpace(sessionId)
            ? [TokenKey(tenantId, jti)]
            : [TokenKey(tenantId, jti), SessionKey(tenantId, sessionId)];
        try
        {
            return await multiplexer.GetDatabase().KeyExistsAsync(keys) > 0;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "Blacklist token tidak dapat dibaca; token dianggap belum dicabut");
            return false;
        }
    }

    private async Task SetAsync(string key, TimeSpan ttl)
    {
        if (redis.Multiplexer is not { } multiplexer)
        {
            logger.LogWarning("Redis tidak dikonfigurasi; pencabutan token tidak disimpan");
            return;
        }

        try
        {
            await multiplexer.GetDatabase().StringSetAsync(key, Marker, ttl);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogError(ex, "Pencabutan token gagal disimpan ke Redis");
        }
    }
}
