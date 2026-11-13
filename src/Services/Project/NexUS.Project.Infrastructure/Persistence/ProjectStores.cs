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

    public Task<ProjectEntity?> FindWithMembersAsync(Guid publicId, CancellationToken ct) =>
        db.Projects.Include(p => p.Members).FirstOrDefaultAsync(p => p.PublicId == publicId, ct);

    public async Task<ProjectRole?> GetMemberRoleAsync(Guid projectId, Guid userId, CancellationToken ct)
    {
        var roles = await (from m in db.ProjectMembers.AsNoTracking()
                           join p in db.Projects.AsNoTracking() on m.ProjectId equals p.Id
                           where p.PublicId == projectId && m.UserId == userId
                           select m.Role).ToListAsync(ct);
        return roles.Count == 0 ? null : roles[0];
    }

    public Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken ct) =>
        db.Projects.AnyAsync(p => p.Code == normalizedCode, ct);

    public async Task AddAsync(ProjectEntity project, CancellationToken ct) => await db.Projects.AddAsync(project, ct);
}

public sealed class ProjectReadService(ProjectDbContext db) : IProjectReadService
{
    public async Task<ProjectDto?> GetAsync(Guid publicId, CancellationToken ct) =>
        await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.PublicId == publicId, ct) is { } project ? ProjectDto.From(project) : null;

    public async Task<PagedResult<ProjectDto>> ListAsync(ProjectFilter filter, CancellationToken ct)
    {
        var (page, size) = (filter.Page, filter.Size); // sudah dibatasi validator

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

        if (filter.MemberUserId is { } memberId)
            query = query.Where(p => db.ProjectMembers.Any(m => m.ProjectId == p.Id && m.UserId == memberId));
        if (filter.OwnerId is { } ownerId) query = query.Where(p => p.OwnerId == ownerId);
        if (filter.DepartmentId is { } departmentId) query = query.Where(p => p.DepartmentId == departmentId);

        var total = await query.LongCountAsync(ct);
        var items = await query.OrderBy(p => p.Name).ThenBy(p => p.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);

        return new PagedResult<ProjectDto>(items.Select(ProjectDto.From).ToList(), page, size, total);
    }

    public async Task<IReadOnlyList<ProjectMemberDto>> ListMembersAsync(Guid projectId, CancellationToken ct)
    {
        var rows = await (from m in db.ProjectMembers.AsNoTracking()
                          join p in db.Projects.AsNoTracking() on m.ProjectId equals p.Id
                          join u in db.UserRefs.AsNoTracking() on m.UserId equals u.UserId into known
                          from u in known.DefaultIfEmpty()
                          where p.PublicId == projectId
                          select new { m.UserId, m.Role, m.JoinedAt, FullName = u != null ? u.FullName : null, Email = u != null ? u.Email : null, IsActive = u != null ? (bool?)u.IsActive : null })
            .ToListAsync(ct);

        return rows
            .OrderBy(r => r.Role).ThenBy(r => r.FullName ?? string.Empty, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.UserId)
            .Select(r => new ProjectMemberDto(r.UserId, r.FullName, r.Email, ProjectRoleNames.ToApi(r.Role), r.JoinedAt, r.IsActive))
            .ToList();
    }
}

public sealed class UserDirectory(ProjectDbContext db) : IUserDirectory
{
    public async Task<bool?> IsActiveAsync(Guid userId, CancellationToken ct)
    {
        var states = await db.UserRefs.AsNoTracking().Where(u => u.UserId == userId).Select(u => u.IsActive).ToListAsync(ct);
        return states.Count == 0 ? null : states[0];
    }
}
