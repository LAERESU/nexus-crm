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
/// hanya boleh satu tingkat. Tugas baru selalu <c>TODO</c>; status berubah lewat <see cref="ChangeStatus"/> mengikuti
/// <see cref="TaskStatusRules"/>, dan setiap perubahan dicatat di riwayat status.
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

    /// <summary>Waktu tugas menjadi <c>DONE</c>; kosong lagi saat dibuka ulang.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    private readonly List<TaskStatusChange> _statusHistory = [];

    /// <summary>Perubahan status baru pada instance ini (riwayat yang tersimpan tidak dimuat).</summary>
    public IReadOnlyCollection<TaskStatusChange> StatusHistory => _statusHistory;

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
        // Versi induk ikut naik, sehingga menghapus induk bersamaan dengan membuat subtask-nya tidak bisa sama-sama lolos
        // (yang kedua bentrok versi, 409) dan tidak tersisa subtask hidup di bawah induk yang terhapus.
        parent?.MarkChildrenChanged();
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

    /// <summary>
    /// Memindah status sesuai <see cref="TaskStatusRules"/>, mencatat riwayat, dan mengisi/mengosongkan
    /// <see cref="CompletedAt"/>. Status yang sama tidak berbuat apa-apa (false), sehingga request yang diulang aman.
    /// </summary>
    public bool ChangeStatus(TaskItemStatus next, Guid actorId, DateTimeOffset now)
    {
        if (IsDeleted) throw new DomainException("Status tugas yang dihapus tidak bisa diubah.");
        if (!Enum.IsDefined(next)) throw new DomainException("Status tugas tidak dikenal.");
        if (actorId == Guid.Empty) throw new DomainException("Pengubah status wajib diisi.");
        if (next == Status) return false;

        if (!TaskStatusRules.CanTransition(Status, next))
        {
            var allowed = TaskStatusRules.AllowedFrom(Status);
            var hint = allowed.Count == 0
                ? $"Tugas berstatus {ApiName(Status)} sudah final."
                : $"Dari {ApiName(Status)} hanya bisa ke: {string.Join(", ", allowed.Select(ApiName))}.";
            throw new DomainException($"Status tugas tidak bisa diubah dari {ApiName(Status)} ke {ApiName(next)}. {hint}");
        }

        var previous = Status;
        Status = next;
        CompletedAt = next == TaskItemStatus.Done ? now : null;
        _statusHistory.Add(new TaskStatusChange(TenantId, previous, next, actorId, now));
        AddDomainEvent(new TaskStatusChangedDomainEvent(TenantId, PublicId, ProjectId, ToName(previous), ToName(next), actorId, now));
        return true;
    }

    private static string ApiName(TaskItemStatus status) => ToName(status).ToLowerInvariant();

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

/// <param name="FromStatus">Nama kolom (huruf besar), mis. <c>IN_PROGRESS</c>.</param>
public sealed record TaskStatusChangedDomainEvent(
    long TenantId, Guid TaskId, Guid ProjectId, string FromStatus, string ToStatus, Guid ChangedBy, DateTimeOffset ChangedAt) : DomainEvent;

/// <summary>Satu baris riwayat status (<c>TASK_STATUS_HISTORY</c>); hanya ditambah, tidak pernah diubah.</summary>
public sealed class TaskStatusChange : Entity
{
    public long TaskId { get; private set; }
    public TaskItemStatus FromStatus { get; private set; }
    public TaskItemStatus ToStatus { get; private set; }

    /// <summary>ID publik user pengubah (milik User Service).</summary>
    public Guid ChangedBy { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    private TaskStatusChange() { } // untuk EF Core

    internal TaskStatusChange(long tenantId, TaskItemStatus from, TaskItemStatus to, Guid changedBy, DateTimeOffset changedAt)
    {
        TenantId = tenantId;
        FromStatus = from;
        ToStatus = to;
        ChangedBy = changedBy;
        ChangedAt = changedAt;
    }
}

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
