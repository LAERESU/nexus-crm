using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using NexUS.BuildingBlocks.Caching;
using StackExchange.Redis;
using ProjectEntity = NexUS.Project.Domain.Projects.Project;

namespace NexUS.Project.Infrastructure.Caching;

/// <summary>
/// Cache-aside proyek di Redis, langsung lewat <see cref="IRedisConnection"/> (tanpa <c>Cache:KeyPrefix</c>) agar kunci
/// selalu berawalan <c>tnt:{tenantId}:</c> dan bisa dibaca service lain.
/// <para>
/// Race cache-aside dicegah dengan <i>tombstone</i>: invalidasi menimpa kunci dengan penanda berumur pendek
/// (<see cref="TombstoneTtl"/>), dan pengisian cache hanya terjadi bila kunci belum ada (<c>SET NX</c>). Pembacaan lambat
/// yang membaca database sebelum perubahan tidak bisa menulis ulang data lama setelah invalidasi.
/// </para>
/// Redis hanya cache: kegagalan dianggap cache miss; data yang tidak bisa dibaca (format lama) juga dianggap miss.
/// </summary>
public sealed class ProjectCache(IRedisConnection redis, ILogger<ProjectCache> logger)
{
    private const string Tombstone = "__invalidated__";

    public static readonly TimeSpan TombstoneTtl = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>False bila Redis tidak dikonfigurasi (Development tanpa Redis): pemanggil langsung ke database.</summary>
    public bool IsEnabled => redis.Multiplexer is not null;

    public async Task<T?> GetAsync<T>(string key) where T : class
    {
        if (redis.Multiplexer is not { } multiplexer) return null;
        try
        {
            var value = await multiplexer.GetDatabase().StringGetAsync(key);
            if (value.IsNullOrEmpty || value == Tombstone)
            {
                logger.LogDebug("Cache miss {CacheKey}", key);
                return null;
            }

            logger.LogDebug("Cache hit {CacheKey}", key);
            return JsonSerializer.Deserialize<T>((string)value!, Json);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Isi cache {CacheKey} tidak dapat dibaca; dianggap miss", key);
            return null;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "Redis GET gagal untuk {CacheKey}", key);
            return null;
        }
    }

    /// <summary>Mengisi cache hanya bila kunci belum ada, sehingga tidak menimpa tombstone dari invalidasi yang baru terjadi.</summary>
    public async Task TrySetAsync<T>(string key, T value, TimeSpan ttl)
    {
        if (redis.Multiplexer is not { } multiplexer) return;
        try
        {
            await multiplexer.GetDatabase().StringSetAsync(key, JsonSerializer.Serialize(value, Json), ttl, When.NotExists);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "Redis SET gagal untuk {CacheKey}", key);
        }
    }

    /// <summary>
    /// Menandai kunci tidak berlaku. Kegagalan dicatat sebagai error: data basi (versi proyek, keanggotaan) bisa terlihat
    /// sampai TTL.
    /// </summary>
    public async Task InvalidateAsync(IReadOnlyCollection<string> keys)
    {
        if (redis.Multiplexer is not { } multiplexer || keys.Count == 0) return;
        try
        {
            var db = multiplexer.GetDatabase();
            await Task.WhenAll(keys.Select(key => db.StringSetAsync(key, Tombstone, TombstoneTtl)));
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogError(ex, "Cache {CacheKeys} tidak dapat dihapus; perubahan baru terlihat setelah TTL", string.Join(", ", keys));
        }
    }
}

/// <summary>
/// Menghapus cache proyek yang berubah setelah simpan berhasil. Proyek yang anggota atau milestone-nya berubah ikut
/// tercatat karena versinya naik (status Modified). Dipasang sebagai interceptor agar DbContext tidak bergantung pada cache.
/// </summary>
public sealed class ProjectCacheInvalidator(ProjectCache cache) : SaveChangesInterceptor
{
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<DbContext, List<string>> _pending = new();

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            var keys = context.ChangeTracker.Entries<ProjectEntity>()
                .Where(e => e.State is EntityState.Modified or EntityState.Deleted)
                .SelectMany(e => new[]
                {
                    ProjectCacheKeys.Project(e.Entity.TenantId, e.Entity.PublicId),
                    ProjectCacheKeys.Members(e.Entity.TenantId, e.Entity.PublicId),
                })
                .Distinct()
                .ToList();
            _pending.AddOrUpdate(context, keys);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context && _pending.TryGetValue(context, out var keys))
        {
            _pending.Remove(context);
            // Sengaja tanpa CancellationToken: perubahan sudah ter-commit, invalidasi tidak boleh terlewat karena request batal.
            await cache.InvalidateAsync(keys);
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (eventData.Context is { } context) _pending.Remove(context);
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context) _pending.Remove(context);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }
}
