using Microsoft.Extensions.Logging;
using NexUS.BuildingBlocks.Caching;
using StackExchange.Redis;

namespace NexUS.BuildingBlocks.Auth;

/// <summary>Satu permission milik user beserta scope penugasan role yang memberikannya (<c>global</c>, <c>department</c>, <c>project</c>).</summary>
public sealed record GrantedPermission(string Code, string ScopeType, Guid? ScopeId);

/// <summary>
/// Akses efektif user. Sumber kebenaran adalah User Service; hasilnya di-cache di Redis (<see cref="IPermissionCache"/>).
/// </summary>
public interface IPermissionService
{
    /// <summary>
    /// Permission user di tenant ini. Melempar exception bila sumber tidak dapat dihubungi (pemanggil wajib fail closed);
    /// user tanpa role menghasilkan daftar kosong.
    /// </summary>
    Task<IReadOnlyList<GrantedPermission>> GetAsync(long tenantId, Guid userId, CancellationToken ct = default);
}

/// <summary>
/// Sumber permission yang otoritatif. User Service memakai database lokalnya; service lain memanggil User Service
/// lewat <c>UserServicePermissionSource</c>.
/// </summary>
public interface IPermissionSource
{
    Task<IReadOnlyList<GrantedPermission>> GetAsync(long tenantId, Guid userId, CancellationToken ct = default);
}

/// <summary>
/// Cache permission di Redis, key <c>tnt:{tenantId}:perm:{userId}</c> dengan TTL 15 menit (ARCHITECTURE.md B.2). Key tetap
/// tanpa <c>Cache:KeyPrefix</c> per service, agar penulis (User Service) dan pembaca (semua service) memakai key yang sama.
/// Redis hanya cache: kegagalan baca dianggap cache miss, sehingga aplikasi tetap berjalan bila Redis mati.
/// </summary>
public interface IPermissionCache
{
    Task<IReadOnlyList<GrantedPermission>?> GetAsync(long tenantId, Guid userId, CancellationToken ct = default);

    Task SetAsync(long tenantId, Guid userId, IReadOnlyList<GrantedPermission> permissions, CancellationToken ct = default);

    /// <summary>Menghapus cache sejumlah user agar perubahan role langsung berlaku tanpa menunggu TTL.</summary>
    Task InvalidateAsync(long tenantId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);
}

public static class PermissionCacheKeys
{
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);

    public static string For(long tenantId, Guid userId) => CacheKeys.Tenant(tenantId, $"perm:{userId:D}");
}

public sealed class RedisPermissionCache(IRedisConnection redis, ILogger<RedisPermissionCache> logger) : IPermissionCache
{
    private const int DeleteBatchSize = 500;

    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<GrantedPermission>?> GetAsync(long tenantId, Guid userId, CancellationToken ct = default)
    {
        if (redis.Multiplexer is not { } multiplexer) return null;

        try
        {
            var value = await multiplexer.GetDatabase().StringGetAsync(PermissionCacheKeys.For(tenantId, userId));
            return value.IsNullOrEmpty
                ? null
                : System.Text.Json.JsonSerializer.Deserialize<List<GrantedPermission>>((string)value!, Json);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "Cache permission tidak dapat dibaca; memakai sumber permission");
            return null;
        }
    }

    public async Task SetAsync(long tenantId, Guid userId, IReadOnlyList<GrantedPermission> permissions, CancellationToken ct = default)
    {
        if (redis.Multiplexer is not { } multiplexer) return;

        try
        {
            await multiplexer.GetDatabase().StringSetAsync(
                PermissionCacheKeys.For(tenantId, userId), System.Text.Json.JsonSerializer.Serialize(permissions, Json), PermissionCacheKeys.Ttl);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "Cache permission tidak dapat disimpan");
        }
    }

    public async Task InvalidateAsync(long tenantId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default)
    {
        if (userIds.Count == 0 || redis.Multiplexer is not { } multiplexer) return;

        try
        {
            // Per key (bukan DEL multi-key) agar tetap jalan di Redis cluster/proxy yang membagi key ke slot berbeda.
            var database = multiplexer.GetDatabase();
            foreach (var batch in userIds.Distinct().Chunk(DeleteBatchSize))
                await Task.WhenAll(batch.Select(id => database.KeyDeleteAsync(PermissionCacheKeys.For(tenantId, id))));
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            // Cache basi paling lama sampai TTL; dicatat sebagai error karena perubahan role tidak langsung berlaku.
            logger.LogError(ex, "Cache permission {UserCount} user gagal dihapus; perubahan role baru berlaku setelah TTL", userIds.Count);
        }
    }
}

/// <summary>Cache-aside: baca Redis, bila kosong ambil dari <see cref="IPermissionSource"/> lalu simpan.</summary>
public sealed class PermissionService(IPermissionCache cache, IPermissionSource source) : IPermissionService
{
    public async Task<IReadOnlyList<GrantedPermission>> GetAsync(long tenantId, Guid userId, CancellationToken ct = default)
    {
        if (await cache.GetAsync(tenantId, userId, ct) is { } cached) return cached;

        var permissions = await source.GetAsync(tenantId, userId, ct);
        await cache.SetAsync(tenantId, userId, permissions, ct); // daftar kosong juga di-cache (user tanpa role)
        return permissions;
    }
}
