using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.BuildingBlocks.Messaging;
using NexUS.User.Application.Common;
using NexUS.User.Domain.Departments;
using NexUS.User.Domain.Tenants;
using NexUS.User.Domain.Users;
using Oracle.ManagedDataAccess.Client;
using UserEntity = NexUS.User.Domain.Users.User;

namespace NexUS.User.Infrastructure.Persistence;

public sealed class UserDbContext(DbContextOptions<UserDbContext> options, TimeProvider timeProvider, ICurrentUser currentUser)
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

    public void ExpectRowVersion(object entity, long expectedRowVersion) =>
        Entry(entity).Property(nameof(Entity.RowVersion)).OriginalValue = expectedRowVersion;

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
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyException($"Data diubah oleh proses lain: {ex.Entries.Count} entitas bentrok.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is OracleException { Number: OracleUniqueViolation })
        {
            // Dua permintaan bersamaan lolos pemeriksaan di handler; index unik di database yang memutuskan.
            throw new DomainException("Data dengan email atau kode yang sama sudah ada di tenant ini.");
        }
    }
}
