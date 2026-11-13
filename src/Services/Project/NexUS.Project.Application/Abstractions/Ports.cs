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
