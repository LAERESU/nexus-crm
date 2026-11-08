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
public sealed class DefaultRoleProvisioner(UserDbContext db)
{
    public async Task ProvisionAllAsync(CancellationToken ct)
    {
        var catalog = await SyncPermissionsAsync(ct);

        // Seeder berjalan tanpa user/request, jadi filter tenant dilewati dan tenant diberikan eksplisit.
        var tenantIds = await db.Tenants.IgnoreQueryFilters().Where(t => !t.IsDeleted).Select(t => t.Id).ToListAsync(ct);
        foreach (var tenantId in tenantIds)
            await ProvisionTenantAsync(tenantId, catalog, ct);
    }

    /// <summary>Menyediakan role bawaan untuk satu tenant; dipanggil juga saat tenant baru dibuat.</summary>
    public async Task ProvisionTenantAsync(long tenantId, CancellationToken ct) =>
        await ProvisionTenantAsync(tenantId, await SyncPermissionsAsync(ct), ct);

    private async Task<IReadOnlyDictionary<string, Permission>> SyncPermissionsAsync(CancellationToken ct)
    {
        var existing = await db.Permissions.ToListAsync(ct);
        foreach (var definition in DefaultRoleCatalog.Permissions.Where(d => existing.All(p => p.Code != d.Code)))
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
        var systemRoles = await db.Roles.IgnoreQueryFilters().Include(r => r.Permissions)
            .Where(r => r.TenantId == tenantId && r.IsSystem && !r.IsDeleted).ToListAsync(ct);

        foreach (var definition in DefaultRoleCatalog.Roles)
        {
            var permissions = definition.Permissions.Select(code => catalog[code]).ToList();
            var role = systemRoles.FirstOrDefault(r => r.Code == definition.Code);

            if (role is null)
                db.Roles.Add(Role.Create(tenantId, definition.Code, definition.Name, definition.Description, permissions, isSystem: true));
            else
                role.SyncSystemPermissions(permissions);
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
