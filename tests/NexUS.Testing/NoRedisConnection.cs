using NexUS.BuildingBlocks.Caching;
using StackExchange.Redis;

namespace NexUS.Testing;

/// <summary>Koneksi Redis yang tidak tersedia: cache kosong, blacklist dan rate limit fail open. Untuk tes tanpa Docker.</summary>
public sealed class NoRedisConnection : IRedisConnection
{
    public IConnectionMultiplexer? Multiplexer => null;
}
