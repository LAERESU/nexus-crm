using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace NexUS.BuildingBlocks.Caching;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Cache Redis. Bila <c>ConnectionStrings:Redis</c> tidak diisi (mis. dijalankan dari IDE tanpa infrastruktur),
    /// dipakai <see cref="NullCacheService"/> agar aplikasi tetap berjalan (CONTEXT.md bagian 3 poin 7).
    /// </summary>
    public static IServiceCollection AddNexusCaching(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CacheOptions>(configuration.GetSection(CacheOptions.SectionName));

        var connectionString = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddSingleton<ICacheService, NullCacheService>();
            return services;
        }

        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(connectionString);
            options.AbortOnConnectFail = false; // start tetap jalan saat Redis mati; tersambung sendiri saat pulih
            return ConnectionMultiplexer.Connect(options);
        });
        services.AddSingleton<ICacheService, RedisCacheService>();
        return services;
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
