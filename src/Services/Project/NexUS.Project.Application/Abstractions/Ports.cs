using NexUS.BuildingBlocks.Common.Results;
using NexUS.Project.Application.Projects;
using ProjectEntity = NexUS.Project.Domain.Projects.Project;

namespace NexUS.Project.Application.Abstractions;

// Semua port otomatis dibatasi ke tenant user yang sedang login (CONTEXT.md bagian 3 poin 8):
// data tenant lain dianggap tidak ada.

public interface IProjectRepository
{
    Task<ProjectEntity?> FindAsync(Guid publicId, CancellationToken ct);

    Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken ct);

    Task AddAsync(ProjectEntity project, CancellationToken ct);
}

public interface IProjectReadService
{
    Task<ProjectDto?> GetAsync(Guid publicId, CancellationToken ct);

    Task<PagedResult<ProjectDto>> ListAsync(ProjectFilter filter, CancellationToken ct);
}

/// <param name="Status">Nama status API; null berarti semua kecuali yang diarsipkan.</param>
public sealed record ProjectFilter(string? Search, string? Status, Guid? OwnerId, Guid? DepartmentId, int Page, int Size);

/// <summary>Kode permission yang diperiksa terhadap scope penugasan role (sama dengan nama policy di Api).</summary>
public static class ProjectPermissions
{
    public const string Create = "project.create";
    public const string Update = "project.update";
}

/// <summary>
/// Pemeriksaan scope atas permission tulis. Policy endpoint hanya memastikan user memegang permission pada scope apa
/// pun; di sini dipastikan scope-nya mencakup objek yang disentuh: global, proyek itu sendiri, atau departemennya.
/// </summary>
public interface IProjectAccess
{
    /// <summary>Melempar <c>ForbiddenException</c> (403) bila tidak ada penugasan yang mencakup proyek/departemen ini.</summary>
    Task EnsureAsync(string permission, Guid? projectId, Guid? departmentId, CancellationToken ct);
}
