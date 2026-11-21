using NexUS.Tasks.Domain.Tasks;

namespace NexUS.Tasks.Application.Tasks;

/// <param name="Status"><c>todo</c>, <c>in_progress</c>, <c>in_review</c>, <c>blocked</c>, <c>done</c>, atau <c>cancelled</c>.</param>
/// <param name="Priority"><c>low</c>, <c>medium</c>, <c>high</c>, atau <c>urgent</c>.</param>
/// <param name="ParentTaskId">ID publik tugas induk bila ini subtask.</param>
/// <param name="RowVersion">Dikirim balik klien lewat header <c>If-Match</c> saat mengubah data.</param>
public sealed record TaskDto(
    Guid Id,
    Guid ProjectId,
    Guid? ParentTaskId,
    string Title,
    string? Description,
    string Status,
    string Priority,
    DateOnly? DueDate,
    Guid ReporterId,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<Guid> AssigneeIds,
    long RowVersion,
    DateTimeOffset CreatedAt)
{
    public static TaskDto From(TaskItem t, Guid? parentPublicId, IReadOnlyList<Guid> assigneeIds) => new(
        t.PublicId, t.ProjectId, parentPublicId, t.Title, t.Description, TaskNames.ToApi(t.Status), TaskNames.ToApi(t.Priority),
        t.DueDate, t.ReporterId, t.CompletedAt, assigneeIds, t.RowVersion, t.CreatedAt);
}

/// <summary>Nama status dan prioritas di API (huruf kecil); kolom memakai huruf besar.</summary>
public static class TaskNames
{
    public const string AllowedStatuses = "todo, in_progress, in_review, blocked, done, cancelled";
    public const string AllowedPriorities = "low, medium, high, urgent";

    public static bool TryParseStatus(string? value, out TaskItemStatus status) => TaskItem.TryParseStatus(value, out status);

    public static string ToApi(TaskItemStatus status) => TaskItem.ToName(status).ToLowerInvariant();

    public static bool TryParsePriority(string? value, out TaskPriority priority) => TaskItem.TryParsePriority(value, out priority);

    public static string ToApi(TaskPriority priority) => TaskItem.ToName(priority).ToLowerInvariant();
}
