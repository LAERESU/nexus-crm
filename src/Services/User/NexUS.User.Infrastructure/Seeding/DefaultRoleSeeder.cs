using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexUS.User.Domain.Roles;
using NexUS.User.Infrastructure.Persistence;

namespace NexUS.User.Infrastructure.Seeding;

/// <summary>
/// Menyamakan katalog permission dan role sistem di database dengan <see cref="DefaultRoleCatalog"/>. Idempoten:
/// aman dijalankan berulang dan oleh beberapa instance sekaligus (bentrokan unique ditangani dengan mengulang).
/// </summary>
public sealed class DefaultRoleProvisioner(UserDbContext db, ILogger<DefaultRoleProvisioner> logger)
{
    public async Task ProvisionAllAsync(CancellationToken ct)
    {
        var catalog = await SyncPermissionsAsync(ct);

        // Seeder berjalan tanpa user/request, jadi filter tenant dilewati dan tenant diberikan eksplisit.
        var tenantIds = await db.Tenants.IgnoreQueryFilters().Where(t => !t.IsDeleted).Select(t => t.Id).ToListAsync(ct);

        // Kegagalan satu tenant tidak boleh menghentikan tenant lain; semua kegagalan dilaporkan di akhir agar seeder mengulang.
        var failures = new List<Exception>();
        foreach (var tenantId in tenantIds)
        {
            try
            {
                await ProvisionTenantAsync(tenantId, catalog, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Seed role bawaan gagal untuk tenant {TenantId}", tenantId);
                failures.Add(ex);
                db.ChangeTracker.Clear(); // buang perubahan setengah jadi milik tenant yang gagal
            }
        }

        if (failures.Count > 0) throw new AggregateException("Seed role bawaan gagal untuk sebagian tenant.", failures);
    }

    /// <summary>
    /// Menyediakan role bawaan untuk satu tenant. Disiapkan untuk use case pembuatan tenant (belum ada); sampai saat itu
    /// tenant baru mendapat role bawaan pada seed startup berikutnya.
    /// </summary>
    public async Task ProvisionTenantAsync(long tenantId, CancellationToken ct) =>
        await ProvisionTenantAsync(tenantId, await SyncPermissionsAsync(ct), ct);

    private async Task<IReadOnlyDictionary<string, Permission>> SyncPermissionsAsync(CancellationToken ct)
    {
        var existing = await db.Permissions.ToListAsync(ct);
        var definitions = DefaultRoleCatalog.Permissions.ToDictionary(d => d.Code);

        // Permission yang dihapus dari katalog dinonaktifkan agar tidak lagi tampil atau bisa diberikan ke role.
        foreach (var obsolete in existing.Where(p => !definitions.ContainsKey(p.Code)).ToList())
        {
            obsolete.MarkDeleted();
            existing.Remove(obsolete);
        }

        foreach (var permission in existing.Where(p => p.Description != definitions[p.Code].Description))
            permission.Describe(definitions[permission.Code].Description);

        foreach (var definition in definitions.Values.Where(d => existing.All(p => p.Code != d.Code)))
        {
            var permission = Permission.Create(definition.Code, definition.Description);
            db.Permissions.Add(permission);
            existing.Add(permission);
        }

        await db.SaveChangesAsync(ct); // ID permission baru dibutuhkan role di langkah berikutnya
        return existing.ToDictionary(p => p.Code);
    }

    private async Task ProvisionTenantAsync(long tenantId, IReadOnlyDictionary<string, Permission> catalog, CancellationToken ct)
    {
        var tenantRoles = await db.Roles.IgnoreQueryFilters().Include(r => r.Permissions)
            .Where(r => r.TenantId == tenantId && !r.IsDeleted).ToListAsync(ct);

        foreach (var definition in DefaultRoleCatalog.Roles)
        {
            var permissions = definition.Permissions.Select(code => catalog[code]).ToList();
            var role = tenantRoles.FirstOrDefault(r => r.Code == definition.Code);

            if (role is null)
                db.Roles.Add(Role.Create(tenantId, definition.Code, definition.Name, definition.Description, permissions, isSystem: true));
            else if (role.IsSystem)
                role.SyncSystemPermissions(permissions);
            else
                // Role kustom lama memakai kode ini (dibuat sebelum kode dicadangkan). Tidak ditimpa dan tidak menggagalkan seed.
                logger.LogWarning("Tenant {TenantId} punya role kustom berkode {Code}; role bawaan dengan kode itu dilewati", tenantId, definition.Code);
        }

        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Menjalankan <see cref="DefaultRoleProvisioner"/> saat startup, mengulang sampai database tersedia.</summary>
public sealed class DefaultRoleSeeder(IServiceScopeFactory scopes, ILogger<DefaultRoleSeeder> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);

    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Selesai setelah seed berhasil; dipakai tes untuk menunggu.</summary>
    public Task Completion => _completion.Task;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<DefaultRoleProvisioner>().ProvisionAllAsync(stoppingToken);
                _completion.TrySetResult();
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Seed role bawaan gagal; mencoba lagi dalam {Delay}", RetryDelay);
                try { await Task.Delay(RetryDelay, stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
}
