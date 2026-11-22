using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.BuildingBlocks.Messaging;
using NexUS.Tasks.Application.Common;
using NexUS.Tasks.Domain.Projects;
using NexUS.Tasks.Domain.Tasks;

namespace NexUS.Tasks.Infrastructure.Persistence;

public sealed class TaskDbContext(DbContextOptions<TaskDbContext> options, TimeProvider timeProvider, ICurrentUser currentUser)
    : DbContext(options), IUnitOfWork
{
    /// <summary>Pelaku sistem untuk kolom CREATED_BY/UPDATED_BY; ID internal user tidak dikenal service ini.</summary>
    public const long SystemActor = 0;

    // Dibaca saat query dijalankan (bukan saat model dibuat), sehingga filter mengikuti user pada request ini.
    private long CurrentTenantId => currentUser.TenantId;

    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<TaskAssignee> TaskAssignees => Set<TaskAssignee>();
    public DbSet<TaskStatusChange> TaskStatusHistory => Set<TaskStatusChange>();
    public DbSet<ProjectRef> ProjectRefs => Set<ProjectRef>();
    public DbSet<ProjectMemberRef> ProjectMemberRefs => Set<ProjectMemberRef>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("TSK_SCHEMA");

        model.Entity<TaskItem>(e =>
        {
            e.ToTable("TASKS");
            MapEntity(e);
            e.Ignore(x => x.DomainEvents);
            e.Property(x => x.ProjectId).HasColumnName("PROJECT_ID");
            e.Property(x => x.ParentTaskId).HasColumnName("PARENT_TASK_ID");
            e.HasOne<TaskItem>().WithMany().HasForeignKey(x => x.ParentTaskId);
            e.Property(x => x.Title).HasColumnName("TITLE").HasMaxLength(TaskItem.TitleMaxLength);
            e.Property(x => x.Description).HasColumnName("DESCRIPTION").HasColumnType("CLOB");
            // Nilai kolom huruf besar sesuai CHECK constraint di V2.
            e.Property(x => x.Status).HasColumnName("STATUS").HasMaxLength(20).HasConversion(
                status => TaskItem.ToName(status),
                value => ParseStatus(value));
            e.Property(x => x.Priority).HasColumnName("PRIORITY").HasMaxLength(10).HasConversion(
                priority => TaskItem.ToName(priority),
                value => Enum.Parse<TaskPriority>(value, true));
            // Provider Oracle belum memetakan DateOnly; kolom DATE diisi lewat DateTime tanpa jam.
            e.Property(x => x.DueDate).HasColumnName("DUE_DATE").HasColumnType("DATE").HasConversion(
                date => date.HasValue ? date.Value.ToDateTime(TimeOnly.MinValue) : (DateTime?)null,
                value => value.HasValue ? DateOnly.FromDateTime(value.Value) : null);
            e.Property(x => x.ReporterId).HasColumnName("REPORTER_ID");
            e.Property(x => x.CompletedAt).HasColumnName("COMPLETED_AT");
            e.HasQueryFilter(x => !x.IsDeleted && x.TenantId == CurrentTenantId);
            // Riwayat baru disimpan lewat navigasi; riwayat lama tidak dimuat bersama tugas.
            e.HasMany(x => x.StatusHistory).WithOne().HasForeignKey(h => h.TaskId);
            e.Navigation(x => x.StatusHistory).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        model.Entity<TaskStatusChange>(e =>
        {
            e.ToTable("TASK_STATUS_HISTORY");
            MapEntity(e);
            e.Property(x => x.TaskId).HasColumnName("TASK_ID");
            e.Property(x => x.FromStatus).HasColumnName("FROM_STATUS").HasMaxLength(20).HasConversion(
                status => TaskItem.ToName(status), value => ParseStatus(value));
            e.Property(x => x.ToStatus).HasColumnName("TO_STATUS").HasMaxLength(20).HasConversion(
                status => TaskItem.ToName(status), value => ParseStatus(value));
            e.Property(x => x.ChangedBy).HasColumnName("CHANGED_BY");
            e.Property(x => x.ChangedAt).HasColumnName("CHANGED_AT");
            e.HasQueryFilter(x => !x.IsDeleted && x.TenantId == CurrentTenantId);
        });

        model.Entity<TaskAssignee>(e =>
        {
            e.ToTable("TASK_ASSIGNEES");
            MapEntity(e);
            e.Property(x => x.TaskId).HasColumnName("TASK_ID");
            e.HasOne<TaskItem>().WithMany().HasForeignKey(x => x.TaskId);
            e.Property(x => x.UserId).HasColumnName("USER_ID");
            e.Property(x => x.AssignedAt).HasColumnName("ASSIGNED_AT");
            e.Property(x => x.AssignedBy).HasColumnName("ASSIGNED_BY");
            e.HasQueryFilter(x => !x.IsDeleted && x.TenantId == CurrentTenantId);
        });

        model.Entity<ProjectRef>(e =>
        {
            e.ToTable("PROJECT_REF");
            MapEntity(e);
            e.Property(x => x.ProjectId).HasColumnName("PROJECT_ID");
            e.Property(x => x.Code).HasColumnName("CODE").HasMaxLength(20);
            e.Property(x => x.Name).HasColumnName("NAME").HasMaxLength(200);
            e.Property(x => x.IsArchived).HasColumnName("IS_ARCHIVED").HasColumnType("NUMBER(1)");
            e.Property(x => x.DetailsEventAt).HasColumnName("DETAILS_EVENT_AT");
            e.HasQueryFilter(x => !x.IsDeleted && x.TenantId == CurrentTenantId);
        });

        model.Entity<ProjectMemberRef>(e =>
        {
            e.ToTable("PROJECT_MEMBER_REFS");
            MapEntity(e);
            e.Property(x => x.ProjectId).HasColumnName("PROJECT_ID");
            e.Property(x => x.UserId).HasColumnName("USER_ID");
            e.Property(x => x.Role).HasColumnName("PROJECT_ROLE").HasMaxLength(20);
            e.Property(x => x.IsRemoved).HasColumnName("IS_REMOVED").HasColumnType("NUMBER(1)");
            e.Property(x => x.LastEventAt).HasColumnName("LAST_EVENT_AT");
            e.HasQueryFilter(x => !x.IsDeleted && x.TenantId == CurrentTenantId);
        });

        model.MapProcessedEvents();
        model.MapOutboxMessages();
    }

    private static TaskItemStatus ParseStatus(string value) =>
        TaskItem.TryParseStatus(value, out var status)
            ? status
            : throw new InvalidOperationException($"Status tugas tidak dikenal di database: {value}");

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

    public void ExpectRowVersion(object entity, long expectedRowVersion) => this.ExpectRowVersionOf(entity, expectedRowVersion);

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Kolom audit, ROW_VERSION, dan outbox (domain event dalam transaksi yang sama).
        this.PrepareSave(timeProvider.GetUtcNow(), SystemActor);

        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyException($"Data diubah oleh proses lain: {ex.Entries.Count} entitas bentrok.");
        }
    }
}
