using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace NexUS.BuildingBlocks.Caching;

/// <summary>Cache-aside di Redis. Redis hanya cache: bila tidak tersedia, aplikasi tetap berjalan.</summary>
public sealed class RedisCacheService(
    IConnectionMultiplexer redis,
    IOptions<CacheOptions> options,
    ILogger<RedisCacheService> logger) : ICacheService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        try
        {
            var value = await redis.GetDatabase().StringGetAsync(Prefixed(key));
            return value.IsNullOrEmpty ? default : JsonSerializer.Deserialize<T>((string)value!, Json);
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            logger.LogWarning(ex, "Redis GET gagal untuk {CacheKey}", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        try
        {
            await redis.GetDatabase().StringSetAsync(
                Prefixed(key),
                JsonSerializer.Serialize(value, Json),
                ttl ?? TimeSpan.FromMinutes(options.Value.DefaultTtlMinutes));
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            logger.LogWarning(ex, "Redis SET gagal untuk {CacheKey}", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await redis.GetDatabase().KeyDeleteAsync(Prefixed(key));
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            logger.LogWarning(ex, "Redis DEL gagal untuk {CacheKey}", key);
        }
    }

    public async Task<T> GetOrSetAsync<T>(
        string key, Func<CancellationToken, Task<T>> factory, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        var cached = await GetAsync<T>(key, ct);
        if (cached is not null) return cached;

        var fresh = await factory(ct);
        if (fresh is not null) await SetAsync(key, fresh, ttl, ct);
        return fresh;
    }

    private string Prefixed(string key) => options.Value.KeyPrefix + key;

    private static bool IsRedisFailure(Exception ex) => ex is RedisException or TimeoutException;
}
