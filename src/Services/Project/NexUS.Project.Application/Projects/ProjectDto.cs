using NexUS.Project.Domain.Projects;
using ProjectEntity = NexUS.Project.Domain.Projects.Project;

namespace NexUS.Project.Application.Projects;

/// <param name="Status"><c>planning</c>, <c>active</c>, <c>on_hold</c>, <c>completed</c>, atau <c>archived</c>.</param>
/// <param name="RowVersion">Dikirim balik klien lewat header <c>If-Match</c> saat mengubah data.</param>
public sealed record ProjectDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    string Status,
    DateOnly? StartDate,
    DateOnly? EndDate,
    Guid OwnerId,
    Guid? DepartmentId,
    long RowVersion,
    DateTimeOffset CreatedAt);

/// <summary>Nama status di API (huruf kecil, mis. <c>on_hold</c>); kolom STATUS memakai huruf besar.</summary>
public static class ProjectStatusNames
{
    public const string Allowed = "planning, active, on_hold, completed, archived";

    public static bool TryParse(string? value, out ProjectStatus status) => ProjectEntity.TryParseStatus(value, out status);

    public static string ToApi(ProjectStatus status) => ProjectEntity.ToName(status).ToLowerInvariant();
}
