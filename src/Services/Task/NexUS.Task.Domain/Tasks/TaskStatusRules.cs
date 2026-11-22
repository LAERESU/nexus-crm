namespace NexUS.Tasks.Domain.Tasks;

/// <summary>
/// State machine status tugas (NX-033). Alur utama <c>TODO → IN_PROGRESS → IN_REVIEW → DONE</c>; status aktif
/// (<c>TODO</c>, <c>IN_PROGRESS</c>, <c>IN_REVIEW</c>) bisa ke dan dari <c>BLOCKED</c>; status aktif dan <c>BLOCKED</c>
/// bisa dibatalkan (<c>CANCELLED</c>, final); <c>DONE → IN_PROGRESS</c> membuka ulang tugas.
/// </summary>
public static class TaskStatusRules
{
    private static readonly TaskItemStatus[] ActiveStatuses = [TaskItemStatus.Todo, TaskItemStatus.InProgress, TaskItemStatus.InReview];

    private static readonly Dictionary<TaskItemStatus, TaskItemStatus[]> Allowed = new()
    {
        [TaskItemStatus.Todo] = [TaskItemStatus.InProgress, TaskItemStatus.Blocked, TaskItemStatus.Cancelled],
        [TaskItemStatus.InProgress] = [TaskItemStatus.InReview, TaskItemStatus.Blocked, TaskItemStatus.Cancelled],
        [TaskItemStatus.InReview] = [TaskItemStatus.Done, TaskItemStatus.Blocked, TaskItemStatus.Cancelled],
        [TaskItemStatus.Blocked] = [.. ActiveStatuses, TaskItemStatus.Cancelled],
        [TaskItemStatus.Done] = [TaskItemStatus.InProgress],
        [TaskItemStatus.Cancelled] = [],
    };

    /// <summary>Status tujuan yang diizinkan dari <paramref name="from"/>, dalam urutan alur kerja.</summary>
    public static IReadOnlyList<TaskItemStatus> AllowedFrom(TaskItemStatus from) =>
        Allowed.TryGetValue(from, out var next) ? next : [];

    public static bool CanTransition(TaskItemStatus from, TaskItemStatus to) => AllowedFrom(from).Contains(to);
}
