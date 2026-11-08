using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Logging;
using NexUS.BuildingBlocks.Auth;
using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.BuildingBlocks.Messaging;
using NexUS.User.Application.Common;
using NexUS.User.Domain.Departments;
using NexUS.User.Domain.Roles;
using NexUS.User.Domain.Tenants;
using NexUS.User.Domain.Users;
using Oracle.ManagedDataAccess.Client;
using UserEntity = NexUS.User.Domain.Users.User;

namespace NexUS.User.Infrastructure.Persistence;

public sealed class UserDbContext(
    DbContextOptions<UserDbContext> options, TimeProvider timeProvider, ICurrentUser currentUser,
    IPermissionCache permissionCache, ILogger<UserDbContext> logger)
    : DbContext(options), IUnitOfWork
{
    /// <summary>Pelaku sistem untuk kolom CREATED_BY/UPDATED_BY; ID internal user belum ada di token.</summary>
    public const long SystemActor = 0;

    private const int OracleUniqueViolation = 1; // ORA-00001

    // Dibaca saat query dijalankan (bukan saat model dibuat), sehingga filter mengikuti user pada request ini.
    private long CurrentTenantId => currentUser.TenantId;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("USR_SCHEMA");

        model.Entity<Tenant>(e =>
        {
            e.ToTable("TENANTS");
            MapEntity(e);
            e.Ignore(x => x.DomainEvents);
            e.Property(x => x.Code).HasColumnName("CODE").HasMaxLength(Tenant.CodeMaxLength);
            e.Property(x => x.Name).HasColumnName("NAME").HasMaxLength(Tenant.NameMaxLength);
            e.Property(x => x.IsActive).HasColumnName("IS_ACTIVE").HasColumnType("NUMBER(1)");
            e.HasIndex(x => x.Code).IsUnique();
            // Tenant adalah akar isolasi: hanya tenant milik user yang terlihat.
            e.HasQueryFilter(x => !x.IsDeleted && x.Id == CurrentTenantId);
        });

        model.Entity<Department>(e =>
        {
            e.ToTable("DEPARTMENTS");
            MapEntity(e);
            e.Ignore(x => x.DomainEvents);
            e.Property(x => x.Code).HasColumnName("CODE").HasMaxLength(Department.CodeMaxLength);
            e.Property(x => x.Name).HasColumnName("NAME").HasMaxLength(Department.NameMaxLength);
            e.Property(x => x.ParentId).HasColumnName("PARENT_ID");
            e.HasOne<Department>().WithMany().HasForeignKey(x => x.ParentId);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            e.HasQueryFilter(x => !x.IsDeleted && x.TenantId == CurrentTenantId);
        });

        model.Entity<UserEntity>(e =>
        {
            e.ToTable("USERS");
            MapEntity(e);
            e.Ignore(x => x.DomainEvents);
            e.Property(x => x.Email).HasColumnName("EMAIL").HasMaxLength(UserEntity.EmailMaxLength);
            e.Property(x => x.FullName).HasColumnName("FULL_NAME").HasMaxLength(UserEntity.FullNameMaxLength);
            e.Property(x => x.JobTitle).HasColumnName("JOB_TITLE").HasMaxLength(UserEntity.JobTitleMaxLength);
            e.Property(x => x.Status).HasColumnName("STATUS").HasConversion<string>(
                status => status == UserStatus.Active ? "ACTIVE" : "INACTIVE",
                value => value == "ACTIVE" ? UserStatus.Active : UserStatus.Inactive).HasMaxLength(20);
            e.Property(x => x.DepartmentId).HasColumnName("DEPARTMENT_ID");
            e.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId);
            e.HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
            e.HasMany(x => x.Roles).WithOne().HasForeignKey(r => r.UserId);
            e.Navigation(x => x.Roles).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.HasQueryFilter(x => !x.IsDeleted && x.TenantId == CurrentTenantId);
        });

        // Katalog permission bersifat global: tanpa filter tenant.
        model.Entity<Permission>(e =>
        {
            e.ToTable("PERMISSIONS");
            MapEntity(e);
            e.Property(x => x.Code).HasColumnName("CODE").HasMaxLength(Permission.CodeMaxLength);
            e.Property(x => x.Module).HasColumnName("MODULE").HasMaxLength(50);
            e.Property(x => x.Description).HasColumnName("DESCRIPTION").HasMaxLength(Permission.DescriptionMaxLength);
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        model.Entity<Role>(e =>
        {
            e.ToTable("ROLES");
            MapEntity(e);
            e.Ignore(x => x.DomainEvents);
            e.Property(x => x.Code).HasColumnName("CODE").HasMaxLength(Role.CodeMaxLength);
            e.Property(x => x.Name).HasColumnName("NAME").HasMaxLength(Role.NameMaxLength);
            e.Property(x => x.Description).HasColumnName("DESCRIPTION").HasMaxLength(Role.DescriptionMaxLength);
            e.Property(x => x.IsSystem).HasColumnName("IS_SYSTEM").HasColumnType("NUMBER(1)");
            e.HasMany(x => x.Permissions).WithOne().HasForeignKey(p => p.RoleId);
            e.Navigation(x => x.Permissions).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.HasQueryFilter(x => !x.IsDeleted && x.TenantId == CurrentTenantId);
        });

        model.Entity<RolePermission>(e =>
        {
            e.ToTable("ROLE_PERMISSIONS");
            MapEntity(e);
            e.Property(x => x.RoleId).HasColumnName("ROLE_ID");
            e.Property(x => x.PermissionId).HasColumnName("PERMISSION_ID");
            e.HasOne<Permission>().WithMany().HasForeignKey(x => x.PermissionId);
            e.HasQueryFilter(x => !x.IsDeleted && x.TenantId == CurrentTenantId);
        });

        model.Entity<UserRole>(e =>
        {
            e.ToTable("USER_ROLES");
            MapEntity(e);
            e.Property(x => x.UserId).HasColumnName("USER_ID");
            e.Property(x => x.RoleId).HasColumnName("ROLE_ID");
            // Nilai kolom huruf besar (GLOBAL/DEPARTMENT/PROJECT) sesuai CHECK constraint di V3.
            e.Property(x => x.ScopeType).HasColumnName("SCOPE_TYPE").HasMaxLength(20).HasConversion(
                scope => scope.ToString().ToUpperInvariant(),
                value => Enum.Parse<RoleScopeType>(value, true));
            e.Property(x => x.ScopeId).HasColumnName("SCOPE_ID");
            e.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId);
            e.HasQueryFilter(x => !x.IsDeleted && x.TenantId == CurrentTenantId);
        });

        model.Entity<OutboxMessage>(e =>
        {
            e.ToTable("OUTBOX_MESSAGES");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("ID").ValueGeneratedNever();
            e.Property(x => x.Type).HasColumnName("TYPE").HasMaxLength(500);
            e.Property(x => x.Payload).HasColumnName("PAYLOAD").HasColumnType("CLOB");
            e.Property(x => x.OccurredAt).HasColumnName("OCCURRED_AT");
            e.Property(x => x.ProcessedAt).HasColumnName("PROCESSED_AT");
            e.Property(x => x.RetryCount).HasColumnName("RETRY_COUNT");
            e.Property(x => x.Error).HasColumnName("ERROR").HasMaxLength(2000);
        });
    }

    /// <summary>Kolom wajib CONTEXT.md bagian 5.</summary>
    private static void MapEntity<T>(EntityTypeBuilder<T> e) where T : Entity
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("ID").ValueGeneratedOnAdd();
        e.Property(x => x.PublicId).HasColumnName("PUBLIC_ID");
        e.Property(x => x.TenantId).HasColumnName("TENANT_ID");
        e.Property(x => x.CreatedAt).HasColumnName("CREATED_AT");
        e.Property(x => x.CreatedBy).HasColumnName("CREATED_BY");
        e.Property(x => x.UpdatedAt).HasColumnName("UPDATED_AT");
        e.Property(x => x.UpdatedBy).HasColumnName("UPDATED_BY");
        e.Property(x => x.RowVersion).HasColumnName("ROW_VERSION").IsConcurrencyToken();
        // Tanpa HasConversion<int>: provider Oracle sudah memetakan bool ke NUMBER(1) (lihat AuthDbContext).
        e.Property(x => x.IsDeleted).HasColumnName("IS_DELETED").HasColumnType("NUMBER(1)");
        e.HasIndex(x => x.PublicId).IsUnique();
    }

    public void ExpectRowVersion(object entity, long expectedRowVersion)
    {
        var rowVersion = Entry(entity).Property(nameof(Entity.RowVersion));

        // Dicek langsung, bukan hanya lewat klausa UPDATE: bila tidak ada perubahan (PUT yang sama) tidak ada UPDATE,
        // tetapi klien dengan versi basi tetap berhak mendapat 409 (CONTEXT.md bagian 6).
        if ((long)rowVersion.CurrentValue! != expectedRowVersion)
            throw new ConcurrencyException("Data sudah diubah oleh proses lain; muat ulang lalu coba lagi.");

        rowVersion.OriginalValue = expectedRowVersion;
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.CreatedBy = SystemActor;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedBy = SystemActor;
                entry.Entity.RowVersion++;
            }
        }

        // Outbox pattern: domain event disimpan dalam transaksi yang sama dengan perubahan data.
        var events = ChangeTracker.Entries<AggregateRoot>().SelectMany(e => e.Entity.PopDomainEvents()).ToList();
        Outbox.AddRange(events.Select(OutboxMessage.From));

        try
        {
            var saved = await base.SaveChangesAsync(cancellationToken);
            await InvalidatePermissionCacheAsync(events);
            return saved;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyException($"Data diubah oleh proses lain: {ex.Entries.Count} entitas bentrok.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is OracleException { Number: OracleUniqueViolation } oracle)
        {
            // Dua permintaan bersamaan lolos pemeriksaan di handler; index unik di database yang memutuskan.
            // Bentrok pada baris penghubung berarti ada penulis lain yang menambah baris yang sama (aman diulang).
            if (oracle.Message.Contains("UX_USER_ROLES_ASSIGNMENT", StringComparison.OrdinalIgnoreCase)
                || oracle.Message.Contains("UX_ROLE_PERMISSIONS_ROLE_PERM", StringComparison.OrdinalIgnoreCase))
                throw new ConcurrencyException("Penugasan diubah oleh proses lain; muat ulang lalu coba lagi.");

            throw new DomainException("Data dengan email atau kode yang sama sudah ada di tenant ini.");
        }
    }

    /// <summary>
    /// Perubahan role langsung berlaku: cache permission user terdampak dihapus setelah commit (RabbitMQ belum ada, jadi
    /// consumer RoleChanged menyusul di NX-028). Best effort: kegagalan tidak membatalkan perubahan yang sudah tersimpan.
    /// </summary>
    private async Task InvalidatePermissionCacheAsync(IReadOnlyCollection<IDomainEvent> events)
    {
        var changes = events.OfType<RoleChangedDomainEvent>().ToList();
        if (changes.Count == 0) return;

        try
        {
            foreach (var tenant in changes.GroupBy(e => e.TenantId))
            {
                var userIds = tenant.Where(e => e.UserId is not null).Select(e => e.UserId!.Value).ToHashSet();

                var roleIds = tenant.Where(e => e.RoleId is not null).Select(e => e.RoleId!.Value).Distinct().ToList();
                if (roleIds.Count > 0)
                {
                    // Pemegang role (termasuk yang akunnya nonaktif) lewat penghubung; filter global dilewati karena
                    // tenant sudah diberikan eksplisit dan perubahan bisa datang dari seeder tanpa user.
                    var holders = await (from ur in UserRoles.IgnoreQueryFilters().AsNoTracking()
                                         join r in Roles.IgnoreQueryFilters().AsNoTracking() on ur.RoleId equals r.Id
                                         join u in Users.IgnoreQueryFilters().AsNoTracking() on ur.UserId equals u.Id
                                         where r.TenantId == tenant.Key && u.TenantId == tenant.Key && roleIds.Contains(r.PublicId)
                                         select u.PublicId).Distinct().ToListAsync();
                    userIds.UnionWith(holders);
                }

                await permissionCache.InvalidateAsync(tenant.Key, userIds);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Cache permission tidak dapat dihapus setelah RoleChanged; perubahan role baru berlaku setelah TTL");
        }
    }
}
