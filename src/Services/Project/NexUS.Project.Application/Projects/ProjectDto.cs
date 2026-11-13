using NexUS.Project.Domain.Projects;
using ProjectEntity = NexUS.Project.Domain.Projects.Project;

namespace NexUS.Project.Application.Projects;

/// <param name="Status"><c>planning</c>, <c>active</c>, <c>on_hold</c>, <c>completed</c>, atau <c>archived</c>.</param>
/// <param name="Priority"><c>low</c>, <c>medium</c>, <c>high</c>, atau <c>urgent</c>.</param>
/// <param name="ProgressPct">Persentase tugas selesai (0–100).</param>
/// <param name="RowVersion">Dikirim balik klien lewat header <c>If-Match</c> saat mengubah data.</param>
public sealed record ProjectDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    string Status,
    string Priority,
    DateOnly? StartDate,
    DateOnly? EndDate,
    int ProgressPct,
    Guid OwnerId,
    Guid? DepartmentId,
    long RowVersion,
    DateTimeOffset CreatedAt)
{
    public static ProjectDto From(ProjectEntity p) => new(
        p.PublicId, p.Code, p.Name, p.Description, ProjectStatusNames.ToApi(p.Status), ProjectPriorityNames.ToApi(p.Priority),
        p.StartDate, p.EndDate, p.ProgressPct, p.OwnerId, p.DepartmentId, p.RowVersion, p.CreatedAt);
}

/// <summary>Nama status di API (huruf kecil, mis. <c>on_hold</c>); kolom STATUS memakai huruf besar.</summary>
public static class ProjectStatusNames
{
    public const string Allowed = "planning, active, on_hold, completed, archived";

    public static bool TryParse(string? value, out ProjectStatus status) => ProjectEntity.TryParseStatus(value, out status);

    public static string ToApi(ProjectStatus status) => ProjectEntity.ToName(status).ToLowerInvariant();
}

/// <summary>Nama prioritas di API (huruf kecil); kolom PRIORITY memakai huruf besar.</summary>
public static class ProjectPriorityNames
{
    public const string Allowed = "low, medium, high, urgent";

    public static bool TryParse(string? value, out ProjectPriority priority) => ProjectEntity.TryParsePriority(value, out priority);

    public static string ToApi(ProjectPriority priority) => ProjectEntity.ToName(priority).ToLowerInvariant();
}
