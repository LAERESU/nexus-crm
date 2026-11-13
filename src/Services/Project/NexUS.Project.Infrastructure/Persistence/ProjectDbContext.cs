using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.BuildingBlocks.Messaging;
using NexUS.Project.Application.Common;
using NexUS.Project.Domain.Projects;
using NexUS.Project.Domain.Users;
using Oracle.ManagedDataAccess.Client;
using ProjectEntity = NexUS.Project.Domain.Projects.Project;

namespace NexUS.Project.Infrastructure.Persistence;

public sealed class ProjectDbContext(DbContextOptions<ProjectDbContext> options, TimeProvider timeProvider, ICurrentUser currentUser)
    : DbContext(options), IUnitOfWork
{
    /// <summary>Pelaku sistem untuk kolom CREATED_BY/UPDATED_BY; ID internal user tidak dikenal service ini.</summary>
    public const long SystemActor = 0;

    private const int OracleUniqueViolation = 1; // ORA-00001
    private const string ProjectCodeIndex = "UX_PROJECTS_TNT_CODE";

    // Dibaca saat query dijalankan (bukan saat model dibuat), sehingga filter mengikuti user pada request ini.
    private long CurrentTenantId => currentUser.TenantId;

    public DbSet<ProjectEntity> Projects => Set<ProjectEntity>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<UserRef> UserRefs => Set<UserRef>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("PRJ_SCHEMA");

        model.Entity<ProjectEntity>(e =>
        {
            e.ToTable("PROJECTS");
            MapEntity(e);
            e.Ignore(x => x.DomainEvents);
            e.Property(x => x.Code).HasColumnName("CODE").HasMaxLength(ProjectEntity.CodeMaxLength);
            e.Property(x => x.Name).HasColumnName("NAME").HasMaxLength(ProjectEntity.NameMaxLength);
            e.Property(x => x.Description).HasColumnName("DESCRIPTION").HasMaxLength(ProjectEntity.DescriptionMaxLength);
            // Nilai kolom huruf besar (PLANNING/ON_HOLD/...) sesuai CHECK constraint di V2.
            e.Property(x => x.Status).HasColumnName("STATUS").HasMaxLength(20).HasConversion(
                status => ProjectEntity.ToName(status),
                value => ParseStatus(value));
            e.Property(x => x.Priority).HasColumnName("PRIORITY").HasMaxLength(10).HasConversion(
                priority => ProjectEntity.ToName(priority),
                value => Enum.Parse<ProjectPriority>(value, true));
            e.Property(x => x.ProgressPct).HasColumnName("PROGRESS_PCT");
            // Provider Oracle belum memetakan DateOnly; kolom DATE diisi lewat DateTime tanpa jam.
            e.Property(x => x.StartDate).HasColumnName("START_DATE").HasColumnType("DATE").HasConversion(
                date => date.HasValue ? date.Value.ToDateTime(TimeOnly.MinValue) : (DateTime?)null,
                value => value.HasValue ? DateOnly.FromDateTime(value.Value) : null);
            e.Property(x => x.EndDate).HasColumnName("END_DATE").HasColumnType("DATE").HasConversion(
                date => date.HasValue ? date.Value.ToDateTime(TimeOnly.MinValue) : (DateTime?)null,
                value => value.HasValue ? DateOnly.FromDateTime(value.Value) : null);
            e.Property(x => x.OwnerId).HasColumnName("OWNER_ID");
            e.Property(x => x.DepartmentId).HasColumnName("DEPARTMENT_ID");
            e.HasMany(x => x.Members).WithOne().HasForeignKey(m => m.ProjectId);
            e.Navigation(x => x.Members).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.HasQueryFilter(x => !x.IsDeleted && x.TenantId == CurrentTenantId);
        });

        model.Entity<ProjectMember>(e =>
        {
            e.ToTable("PROJECT_MEMBERS");
            MapEntity(e);
            e.Property(x => x.ProjectId).HasColumnName("PROJECT_ID");
            e.Property(x => x.UserId).HasColumnName("USER_ID");
            e.Property(x => x.Role).HasColumnName("PROJECT_ROLE").HasMaxLength(20).HasConversion(
                role => ProjectMember.ToName(role),
                value => Enum.Parse<ProjectRole>(value, true));
            e.Property(x => x.JoinedAt).HasColumnName("JOINED_AT");
            e.HasQueryFilter(x => !x.IsDeleted && x.TenantId == CurrentTenantId);
        });

        model.Entity<UserRef>(e =>
        {
            e.ToTable("USER_REF");
            MapEntity(e);
            e.Property(x => x.UserId).HasColumnName("USER_ID");
            e.Property(x => x.Email).HasColumnName("EMAIL").HasMaxLength(320);
            e.Property(x => x.FullName).HasColumnName("FULL_NAME").HasMaxLength(200);
            e.Property(x => x.IsActive).HasColumnName("IS_ACTIVE").HasColumnType("NUMBER(1)");
            e.Property(x => x.LastEventAt).HasColumnName("LAST_EVENT_AT");
            e.HasQueryFilter(x => !x.IsDeleted && x.TenantId == CurrentTenantId);
        });

        model.Entity<ProcessedEvent>(e =>
        {
            e.ToTable("PROCESSED_EVENTS");
            e.HasKey(x => new { x.EventId, x.Consumer });
            e.Property(x => x.EventId).HasColumnName("EVENT_ID");
            e.Property(x => x.Consumer).HasColumnName("CONSUMER").HasMaxLength(200);
            e.Property(x => x.ProcessedAt).HasColumnName("PROCESSED_AT");
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

    private static ProjectStatus ParseStatus(string value) =>
        ProjectEntity.TryParseStatus(value, out var status)
            ? status
            : throw new InvalidOperationException($"Status proyek tidak dikenal di database: {value}");

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
        // Perubahan anggota ikut menaikkan ROW_VERSION proyeknya, sehingga dua perubahan anggota yang bersamaan
        // (mis. dua manager saling mengeluarkan) tidak bisa sama-sama lolos: yang kedua bentrok versi (409).
        var touchedProjectIds = ChangeTracker.Entries<ProjectMember>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified).Select(e => e.Entity.ProjectId).ToHashSet();
        foreach (var project in ChangeTracker.Entries<ProjectEntity>())
        {
            if (project.State == EntityState.Unchanged && project.Entity.Id != 0 && touchedProjectIds.Contains(project.Entity.Id))
                project.State = EntityState.Modified;
        }

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

        // Outbox pattern: domain event disimpan dalam transaksi yang sama dengan perubahan data. Pengirimnya ke
        // RabbitMQ (relay) dipasang di NX-028.
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
        catch (DbUpdateException ex) when (ex.InnerException is OracleException { Number: OracleUniqueViolation } oracle
            && oracle.Message.Contains(ProjectCodeIndex, StringComparison.OrdinalIgnoreCase))
        {
            // Dua permintaan bersamaan lolos pemeriksaan di handler; index unik di database yang memutuskan.
            // Pelanggaran constraint unik lain bukan kesalahan klien dan diteruskan sebagai error server.
            throw new DomainException("Kode proyek sudah dipakai di tenant ini.");
        }
    }
}
