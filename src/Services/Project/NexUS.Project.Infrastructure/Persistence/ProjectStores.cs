using Microsoft.EntityFrameworkCore;
using NexUS.BuildingBlocks.Common.Results;
using NexUS.Project.Application.Abstractions;
using NexUS.Project.Application.Projects;
using NexUS.Project.Domain.Projects;
using ProjectEntity = NexUS.Project.Domain.Projects.Project;

namespace NexUS.Project.Infrastructure.Persistence;

// Filter TENANT_ID dipasang sebagai global query filter di ProjectDbContext, sehingga semua query di sini
// otomatis dibatasi ke tenant user yang sedang login.

public sealed class ProjectRepository(ProjectDbContext db) : IProjectRepository
{
    public Task<ProjectEntity?> FindAsync(Guid publicId, CancellationToken ct) =>
        db.Projects.FirstOrDefaultAsync(p => p.PublicId == publicId, ct);

    public Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken ct) =>
        db.Projects.AnyAsync(p => p.Code == normalizedCode, ct);

    public async Task AddAsync(ProjectEntity project, CancellationToken ct) => await db.Projects.AddAsync(project, ct);
}

public sealed class ProjectReadService(ProjectDbContext db) : IProjectReadService
{
    public async Task<ProjectDto?> GetAsync(Guid publicId, CancellationToken ct) =>
        await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.PublicId == publicId, ct) is { } project ? ToDto(project) : null;

    public async Task<PagedResult<ProjectDto>> ListAsync(ProjectFilter filter, CancellationToken ct)
    {
        var page = PagedResult<ProjectDto>.NormalizePage(filter.Page);
        var size = PagedResult<ProjectDto>.NormalizeSize(filter.Size);

        var query = db.Projects.AsNoTracking();

        // Tanpa filter status, proyek yang diarsipkan tidak ikut tampil.
        if (ProjectStatusNames.TryParse(filter.Status, out var status))
            query = query.Where(p => p.Status == status);
        else
            query = query.Where(p => p.Status != ProjectStatus.Archived);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            // Pencarian tidak peka huruf besar/kecil; CODE sudah huruf besar.
            var term = filter.Search.ToLowerInvariant();
            var code = filter.Search.ToUpperInvariant();
            query = query.Where(p => p.Name.ToLower().Contains(term) || p.Code.Contains(code));
        }

        if (filter.OwnerId is { } ownerId) query = query.Where(p => p.OwnerId == ownerId);
        if (filter.DepartmentId is { } departmentId) query = query.Where(p => p.DepartmentId == departmentId);

        var total = await query.LongCountAsync(ct);
        var items = await query.OrderBy(p => p.Name).ThenBy(p => p.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);

        return new PagedResult<ProjectDto>(items.Select(ToDto).ToList(), page, size, total);
    }

    private static ProjectDto ToDto(ProjectEntity p) => new(
        p.PublicId, p.Code, p.Name, p.Description, ProjectStatusNames.ToApi(p.Status), p.StartDate, p.EndDate,
        p.OwnerId, p.DepartmentId, p.RowVersion, p.CreatedAt);
}
