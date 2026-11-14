using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.BuildingBlocks.Messaging;
using NexUS.Project.Application.Common;
using NexUS.Project.Domain.Milestones;
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
    private const string ProjectMemberIndex = "UX_PROJECT_MEMBERS_PROJECT_USER";

    // Dibaca saat query dijalankan (bukan saat model dibuat), sehingga filter mengikuti user pada request ini.
    private long CurrentTenantId => currentUser.TenantId;

    public DbSet<ProjectEntity> Projects => Set<ProjectEntity>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<Milestone> Milestones => Set<Milestone>();
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

        model.Entity<Milestone>(e =>
        {
            e.ToTable("MILESTONES");
            MapEntity(e);
            e.Ignore(x => x.DomainEvents);
            e.Property(x => x.ProjectId).HasColumnName("PROJECT_ID");
            e.Property(x => x.Name).HasColumnName("NAME").HasMaxLength(Milestone.NameMaxLength);
            e.Property(x => x.Description).HasColumnName("DESCRIPTION").HasMaxLength(Milestone.DescriptionMaxLength);
            e.Property(x => x.DueDate).HasColumnName("DUE_DATE").HasColumnType("DATE").HasConversion(
                date => date.ToDateTime(TimeOnly.MinValue),
                value => DateOnly.FromDateTime(value));
            e.Property(x => x.Status).HasColumnName("STATUS").HasMaxLength(20).HasConversion(
                status => Milestone.ToName(status),
                value => Enum.Parse<MilestoneStatus>(value, true));
            e.HasOne<ProjectEntity>().WithMany().HasForeignKey(x => x.ProjectId);
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
            e.Property(x => x.ProfileEventAt).HasColumnName("PROFILE_EVENT_AT");
            e.Property(x => x.StatusEventAt).HasColumnName("STATUS_EVENT_AT");
            e.HasQueryFilter(x => !x.IsDeleted && x.TenantId == CurrentTenantId);
        });

        model.MapProcessedEvents();

        model.MapOutboxMessages();
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
        // Kolom audit, ROW_VERSION (termasuk aggregate yang anaknya berubah), dan outbox. Event dikirim ke RabbitMQ
        // oleh relay (OutboxPublisherWorker).
        var prepared = this.PrepareSave(timeProvider.GetUtcNow(), SystemActor);

        try
        {
            var saved = await base.SaveChangesAsync(cancellationToken);
            prepared.Complete();
            return saved;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyException($"Data diubah oleh proses lain: {ex.Entries.Count} entitas bentrok.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is OracleException { Number: OracleUniqueViolation } member
            && member.Message.Contains(ProjectMemberIndex, StringComparison.OrdinalIgnoreCase))
        {
            // Dua permintaan bersamaan menambahkan user yang sama.
            throw new DomainException("User sudah menjadi anggota proyek ini.");
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
