using NexUS.BuildingBlocks.Caching;
using StackExchange.Redis;

namespace NexUS.Gateway.Api.RateLimiting;

/// <summary>Hasil pemeriksaan; <see cref="Policy"/> dan <see cref="RetryAfter"/> diisi bila ditolak.</summary>
public readonly record struct RateLimitDecision(bool Allowed, string? Policy, TimeSpan RetryAfter)
{
    public static readonly RateLimitDecision Allow = new(true, null, TimeSpan.Zero);
}

/// <summary>Satu kebijakan yang dievaluasi dalam satu skrip.</summary>
public sealed record RateLimitRule(string Policy, RateLimitAlgorithm Algorithm, string Key, WindowLimit Limit);

public enum RateLimitAlgorithm
{
    FixedWindow = 1,
    SlidingLog = 2,
    TokenBucket = 3,
}

/// <summary>
/// Rate limiter dengan hitungan di Redis agar berlaku untuk semua replika Gateway (ADR-0008). Semua kebijakan untuk
/// satu request dievaluasi dalam <b>satu</b> skrip Lua atomik: kuota hanya dipakai bila semua kebijakan lolos,
/// sehingga request yang ditolak satu kebijakan tidak menguras kebijakan lain (mis. kuota tenant). Jam diambil dari
/// Redis (<c>TIME</c>) agar replika dengan jam berbeda tetap konsisten.
/// Redis hanya penyimpan sementara: bila tidak terjangkau, request diizinkan (fail open) dan pemeriksaan dilewati
/// selama <see cref="BreakDuration"/> agar request tidak menunggu timeout berulang kali.
/// </summary>
public sealed class RedisRateLimiter(IRedisConnection redis, TimeProvider timeProvider, ILogger<RedisRateLimiter> logger)
{
    public static readonly TimeSpan BreakDuration = TimeSpan.FromSeconds(30);

    // ARGV: id unik request, lalu per aturan: algoritma, batas, window (ms). KEYS: satu per aturan.
    // Tahap 1 hanya membaca; tahap 2 menulis bila semua lolos. Mengembalikan {1} atau {0, indeksAturan, retryAfterMs}.
    private const string Script = """
        local t = redis.call('TIME')
        local now = t[1] * 1000 + math.floor(t[2] / 1000)
        local id = ARGV[1]
        local n = #KEYS
        local plan = {}
        for i = 1, n do
          local algo = tonumber(ARGV[2 + (i - 1) * 3])
          local limit = tonumber(ARGV[3 + (i - 1) * 3])
          local window = tonumber(ARGV[4 + (i - 1) * 3])
          local key = KEYS[i]
          if algo == 1 then
            local bucket = key .. ':' .. math.floor(now / window)
            local count = tonumber(redis.call('GET', bucket) or '0')
            if count + 1 > limit then
              local ttl = redis.call('PTTL', bucket)
              if ttl < 0 then ttl = window end
              return {0, i, ttl}
            end
            plan[i] = {algo, bucket, window}
          elseif algo == 2 then
            redis.call('ZREMRANGEBYSCORE', key, '-inf', now - window)
            if redis.call('ZCARD', key) + 1 > limit then
              local oldest = redis.call('ZRANGE', key, 0, 0, 'WITHSCORES')
              return {0, i, math.max(1, tonumber(oldest[2]) + window - now)}
            end
            plan[i] = {algo, key, window}
          else
            local rate = limit / window
            local state = redis.call('HMGET', key, 'tokens', 'ts')
            local tokens = tonumber(state[1]) or limit
            local ts = tonumber(state[2]) or now
            tokens = math.min(limit, tokens + (now - ts) * rate)
            if tokens < 1 then
              return {0, i, math.ceil((1 - tokens) / rate)}
            end
            plan[i] = {algo, key, window, tokens - 1}
          end
        end
        for i = 1, n do
          local p = plan[i]
          if p[1] == 1 then
            if redis.call('INCR', p[2]) == 1 then redis.call('PEXPIRE', p[2], p[3]) end
          elseif p[1] == 2 then
            redis.call('ZADD', p[2], now, id)
            redis.call('PEXPIRE', p[2], p[3])
          else
            redis.call('HSET', p[2], 'tokens', tostring(p[4]), 'ts', tostring(now))
            redis.call('PEXPIRE', p[2], p[3] * 2)
          end
        end
        return {1}
        """;

    private long _breakUntilTicks;

    public async Task<RateLimitDecision> EvaluateAsync(IReadOnlyList<RateLimitRule> rules)
    {
        if (rules.Count == 0 || redis.Multiplexer is not { } multiplexer) return RateLimitDecision.Allow;
        if (timeProvider.GetUtcNow().UtcTicks < Interlocked.Read(ref _breakUntilTicks)) return RateLimitDecision.Allow;

        var keys = rules.Select(r => (RedisKey)r.Key).ToArray();
        var args = new List<RedisValue> { Guid.NewGuid().ToString("N") };
        foreach (var rule in rules)
        {
            args.Add((int)rule.Algorithm);
            args.Add(rule.Limit.PermitLimit);
            args.Add((long)rule.Limit.Window.TotalMilliseconds);
        }

        try
        {
            var result = (RedisResult[])(await multiplexer.GetDatabase().ScriptEvaluateAsync(Script, keys, args.ToArray()))!;
            if ((long)result[0] == 1) return RateLimitDecision.Allow;

            var rule = rules[(int)(long)result[1] - 1];
            return new RateLimitDecision(false, rule.Policy, TimeSpan.FromMilliseconds((long)result[2]));
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            Interlocked.Exchange(ref _breakUntilTicks, (timeProvider.GetUtcNow() + BreakDuration).UtcTicks);
            logger.LogWarning(ex, "Rate limit tidak dapat diperiksa (Redis); request diizinkan dan pemeriksaan dilewati {BreakSeconds} detik",
                BreakDuration.TotalSeconds);
            return RateLimitDecision.Allow;
        }
    }
}
