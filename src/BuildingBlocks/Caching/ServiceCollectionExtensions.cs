using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace NexUS.BuildingBlocks.Caching;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Cache Redis. Connection string dibaca saat service pertama kali dipakai (bukan saat registrasi), sehingga
    /// konfigurasi per environment dan override di tes selalu berlaku.
    /// </summary>
    public static IServiceCollection AddNexusCaching(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CacheOptions>(configuration.GetSection(CacheOptions.SectionName));
        services.TryAddSingleton<IRedisConnection, RedisConnection>();
        services.TryAddSingleton<ICacheService>(sp =>
            sp.GetRequiredService<IRedisConnection>().Multiplexer is { } multiplexer
                ? new RedisCacheService(multiplexer, sp.GetRequiredService<IOptions<CacheOptions>>(),
                    sp.GetRequiredService<ILogger<RedisCacheService>>())
                : new NullCacheService());
        return services;
    }
}

/// <summary>Koneksi Redis bersama satu proses; <see cref="Multiplexer"/> null bila Redis tidak dikonfigurasi.</summary>
public interface IRedisConnection
{
    IConnectionMultiplexer? Multiplexer { get; }
}

/// <summary>
/// Tanpa <c>ConnectionStrings:Redis</c>, Development tetap berjalan tanpa cache (mis. dari IDE). Di environment
/// lain aplikasi gagal start, karena fitur keamanan seperti blacklist token bergantung pada Redis dan tidak boleh
/// diam-diam mati akibat konfigurasi yang terlewat.
/// </summary>
public sealed class RedisConnection : IRedisConnection, IDisposable
{
    private readonly Lazy<ConnectionMultiplexer>? _multiplexer;

    public RedisConnection(IConfiguration configuration, IHostEnvironment environment, ILogger<RedisConnection> logger)
    {
        var connectionString = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            if (!environment.IsDevelopment())
                throw new InvalidOperationException("ConnectionStrings:Redis wajib diisi di luar Development.");

            logger.LogWarning("ConnectionStrings:Redis kosong; cache dan blacklist token dinonaktifkan (hanya Development)");
            return;
        }

        var options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = false; // tetap jalan saat Redis mati; tersambung sendiri saat pulih
        _multiplexer = new Lazy<ConnectionMultiplexer>(() => ConnectionMultiplexer.Connect(options));
    }

    public IConnectionMultiplexer? Multiplexer => _multiplexer?.Value;

    public void Dispose()
    {
        if (_multiplexer is { IsValueCreated: true }) _multiplexer.Value.Dispose();
    }
}

/// <summary>Cache yang selalu kosong: semua baca menghasilkan default, semua tulis diabaikan.</summary>
public sealed class NullCacheService : ICacheService
{
    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) => Task.FromResult<T?>(default);

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default) => Task.CompletedTask;

    public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;

    public Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan? ttl = null, CancellationToken ct = default) =>
        factory(ct);
}
