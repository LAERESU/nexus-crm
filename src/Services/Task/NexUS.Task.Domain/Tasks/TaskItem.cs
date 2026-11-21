using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;

namespace NexUS.Tasks.Domain.Tasks;

public enum TaskItemStatus
{
    Todo,
    InProgress,
    InReview,
    Blocked,
    Done,
    Cancelled,
}

public enum TaskPriority
{
    Low,
    Medium,
    High,
    Urgent,
}

/// <summary>
/// Tugas dalam satu proyek (ARCHITECTURE.md D.3). Subtask menunjuk tugas induk lewat <see cref="ParentTaskId"/> dan
/// hanya boleh satu tingkat. Status berubah lewat state machine (NX-033); di sini tugas baru selalu <c>TODO</c>.
/// </summary>
public sealed class TaskItem : AggregateRoot
{
    public const int TitleMaxLength = 300;
    public const int DescriptionMaxLength = 20_000;

    /// <summary>ID publik proyek (milik Project Service).</summary>
    public Guid ProjectId { get; private set; }

    /// <summary>ID internal tugas induk; null untuk tugas tingkat atas.</summary>
    public long? ParentTaskId { get; private set; }

    public string Title { get; private set; } = default!;
    public string? Description { get; private set; }
    public TaskItemStatus Status { get; private set; }
    public TaskPriority Priority { get; private set; }
    public DateOnly? DueDate { get; private set; }

    /// <summary>ID publik user pembuat (milik User Service).</summary>
    public Guid ReporterId { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    private TaskItem() { } // untuk EF Core

    /// <param name="parent">Tugas induk bila membuat subtask; harus tugas tingkat atas di proyek yang sama.</param>
    public static TaskItem Create(
        long tenantId, Guid projectId, TaskItem? parent, string title, string? description, TaskPriority priority,
        DateOnly? dueDate, Guid reporterId)
    {
        if (tenantId <= 0) throw new DomainException("Tenant wajib diisi.");
        if (projectId == Guid.Empty) throw new DomainException("Proyek wajib diisi.");
        if (reporterId == Guid.Empty) throw new DomainException("Pembuat tugas wajib diisi.");

        if (parent is not null)
        {
            if (parent.IsDeleted) throw new DomainException("Tugas induk sudah dihapus.");
            if (parent.ProjectId != projectId) throw new DomainException("Subtask harus berada di proyek yang sama dengan induknya.");
            if (parent.ParentTaskId is not null) throw new DomainException("Subtask dari subtask tidak diizinkan (maksimum satu tingkat).");
        }

        var task = new TaskItem
        {
            TenantId = tenantId,
            ProjectId = projectId,
            ParentTaskId = parent?.Id,
            Status = TaskItemStatus.Todo,
            ReporterId = reporterId,
        };
        task.SetDetails(title, description, priority, dueDate);
        task.AddDomainEvent(new TaskCreatedDomainEvent(tenantId, task.PublicId, projectId, parent?.PublicId, task.Title, reporterId));
        return task;
    }

    /// <summary>Mengubah isi tugas; tanpa perubahan nilai tidak ada event.</summary>
    public void Update(string title, string? description, TaskPriority priority, DateOnly? dueDate)
    {
        if (IsDeleted) throw new DomainException("Tugas yang dihapus tidak bisa diubah.");

        var before = (Title, Description, Priority, DueDate);
        SetDetails(title, description, priority, dueDate);
        if (before != (Title, Description, Priority, DueDate))
            AddDomainEvent(new TaskUpdatedDomainEvent(TenantId, PublicId, ProjectId, Title));
    }

    /// <summary>Soft delete; mengulang tidak berbuat apa-apa.</summary>
    public void Delete()
    {
        if (IsDeleted) return;
        MarkDeleted();
        AddDomainEvent(new TaskDeletedDomainEvent(TenantId, PublicId, ProjectId));
    }

    private void SetDetails(string title, string? description, TaskPriority priority, DateOnly? dueDate)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new DomainException("Judul tugas wajib diisi.");
        if (title.Trim().Length > TitleMaxLength) throw new DomainException($"Judul tugas maksimal {TitleMaxLength} karakter.");
        var trimmed = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (trimmed is { Length: > DescriptionMaxLength })
            throw new DomainException($"Deskripsi tugas maksimal {DescriptionMaxLength} karakter.");
        if (!Enum.IsDefined(priority)) throw new DomainException("Prioritas tugas tidak dikenal.");

        Title = title.Trim();
        Description = trimmed;
        Priority = priority;
        DueDate = dueDate;
    }

    /// <summary>Nama status di kolom STATUS (huruf besar, mis. <c>IN_PROGRESS</c>).</summary>
    public static string ToName(TaskItemStatus status) => status switch
    {
        TaskItemStatus.Todo => "TODO",
        TaskItemStatus.InProgress => "IN_PROGRESS",
        TaskItemStatus.InReview => "IN_REVIEW",
        TaskItemStatus.Blocked => "BLOCKED",
        TaskItemStatus.Done => "DONE",
        TaskItemStatus.Cancelled => "CANCELLED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseStatus(string? name, out TaskItemStatus status) => TryParse(name, ToName, out status);

    public static string ToName(TaskPriority priority) => priority.ToString().ToUpperInvariant();

    public static bool TryParsePriority(string? name, out TaskPriority priority) => TryParse(name, ToName, out priority);

    private static bool TryParse<T>(string? name, Func<T, string> toName, out T value) where T : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<T>())
        {
            if (!string.Equals(toName(candidate), name?.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            value = candidate;
            return true;
        }

        value = default;
        return false;
    }
}

public sealed record TaskCreatedDomainEvent(long TenantId, Guid TaskId, Guid ProjectId, Guid? ParentTaskId, string Title, Guid ReporterId)
    : DomainEvent;

public sealed record TaskUpdatedDomainEvent(long TenantId, Guid TaskId, Guid ProjectId, string Title) : DomainEvent;

public sealed record TaskDeletedDomainEvent(long TenantId, Guid TaskId, Guid ProjectId) : DomainEvent;

/// <summary>Assignee satu tugas. Endpoint pengaturannya menyusul di NX-034.</summary>
public sealed class TaskAssignee : Entity
{
    public long TaskId { get; private set; }

    /// <summary>ID publik user (milik User Service).</summary>
    public Guid UserId { get; private set; }

    public DateTimeOffset AssignedAt { get; private set; }
    public Guid AssignedBy { get; private set; }

    private TaskAssignee() { } // untuk EF Core
}
