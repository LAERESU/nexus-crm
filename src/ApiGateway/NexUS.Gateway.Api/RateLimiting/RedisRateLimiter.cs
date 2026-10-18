using NexUS.BuildingBlocks.Caching;
using StackExchange.Redis;

namespace NexUS.Gateway.Api.RateLimiting;

/// <summary>Hasil satu pemeriksaan; <see cref="RetryAfter"/> diisi bila ditolak.</summary>
public readonly record struct RateLimitDecision(bool Allowed, TimeSpan RetryAfter)
{
    public static readonly RateLimitDecision Allow = new(true, TimeSpan.Zero);
}

/// <summary>
/// Rate limiter dengan hitungan di Redis agar berlaku untuk semua replika Gateway. Setiap algoritma berupa satu skrip
/// Lua (atomik) yang memakai jam Redis (<c>TIME</c>), sehingga replika dengan jam berbeda tetap konsisten.
/// Redis hanya penyimpan sementara: bila tidak terjangkau, request diizinkan (fail open) dan kejadiannya dicatat.
/// </summary>
public sealed class RedisRateLimiter(IRedisConnection redis, ILogger<RedisRateLimiter> logger)
{
    // KEYS[1]=prefix; ARGV[1]=batas; ARGV[2]=window (ms). Mengembalikan {diizinkan(1/0), retryAfterMs}.
    private const string FixedWindowScript = """
        local t = redis.call('TIME')
        local now = t[1] * 1000 + math.floor(t[2] / 1000)
        local window = tonumber(ARGV[2])
        local key = KEYS[1] .. ':' .. math.floor(now / window)
        local count = redis.call('INCR', key)
        if count == 1 then redis.call('PEXPIRE', key, window) end
        if count > tonumber(ARGV[1]) then
          local ttl = redis.call('PTTL', key)
          if ttl < 0 then ttl = window end
          return {0, ttl}
        end
        return {1, 0}
        """;

    // Sliding window terboboti dua window (perkiraan yang umum dipakai): hitungan window sebelumnya diberi bobot
    // sesuai porsi yang masih tumpang tindih. Request yang ditolak tidak ikut dihitung.
    private const string SlidingWindowScript = """
        local t = redis.call('TIME')
        local now = t[1] * 1000 + math.floor(t[2] / 1000)
        local limit = tonumber(ARGV[1])
        local window = tonumber(ARGV[2])
        local current = math.floor(now / window)
        local elapsed = now - current * window
        local currentKey = KEYS[1] .. ':' .. current
        local previous = tonumber(redis.call('GET', KEYS[1] .. ':' .. (current - 1)) or '0')
        local count = tonumber(redis.call('GET', currentKey) or '0')
        local estimated = previous * ((window - elapsed) / window) + count
        if estimated + 1 > limit then
          return {0, window - elapsed}
        end
        redis.call('INCR', currentKey)
        redis.call('PEXPIRE', currentKey, window * 2)
        return {1, 0}
        """;

    // Token bucket: kapasitas = batas, terisi batas/periode per milidetik.
    private const string TokenBucketScript = """
        local t = redis.call('TIME')
        local now = t[1] * 1000 + math.floor(t[2] / 1000)
        local capacity = tonumber(ARGV[1])
        local period = tonumber(ARGV[2])
        local rate = capacity / period
        local state = redis.call('HMGET', KEYS[1], 'tokens', 'ts')
        local tokens = tonumber(state[1]) or capacity
        local ts = tonumber(state[2]) or now
        tokens = math.min(capacity, tokens + (now - ts) * rate)
        local allowed = 0
        local retry = 0
        if tokens >= 1 then
          tokens = tokens - 1
          allowed = 1
        else
          retry = math.ceil((1 - tokens) / rate)
        end
        redis.call('HSET', KEYS[1], 'tokens', tostring(tokens), 'ts', tostring(now))
        redis.call('PEXPIRE', KEYS[1], period * 2)
        return {allowed, retry}
        """;

    public Task<RateLimitDecision> FixedWindowAsync(string key, WindowLimit limit) => EvaluateAsync(FixedWindowScript, key, limit);

    public Task<RateLimitDecision> SlidingWindowAsync(string key, WindowLimit limit) => EvaluateAsync(SlidingWindowScript, key, limit);

    public Task<RateLimitDecision> TokenBucketAsync(string key, WindowLimit limit) => EvaluateAsync(TokenBucketScript, key, limit);

    private async Task<RateLimitDecision> EvaluateAsync(string script, string key, WindowLimit limit)
    {
        if (redis.Multiplexer is not { } multiplexer) return RateLimitDecision.Allow;

        try
        {
            var result = (RedisResult[])(await multiplexer.GetDatabase().ScriptEvaluateAsync(
                script, [key], [limit.PermitLimit, (long)limit.Window.TotalMilliseconds]))!;
            return (long)result[0] == 1
                ? RateLimitDecision.Allow
                : new RateLimitDecision(false, TimeSpan.FromMilliseconds((long)result[1]));
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "Rate limit tidak dapat diperiksa (Redis); request diizinkan");
            return RateLimitDecision.Allow;
        }
    }
}
