using Microsoft.EntityFrameworkCore;
using NexUS.BuildingBlocks.Common.Results;
using NexUS.Project.Application.Abstractions;
using NexUS.Project.Application.Milestones;
using NexUS.Project.Application.Projects;
using NexUS.Project.Domain.Milestones;
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

    public async Task<PagedResult<ProjectMemberDto>> ListMembersAsync(Guid projectId, int page, int size, CancellationToken ct)
    {
        var query = MemberRows(projectId);
        var total = await query.LongCountAsync(ct);
        // PROJECT_ROLE berisi MANAGER/MEMBER/VIEWER: urut abjadnya sama dengan urut peran.
        var rows = await query.OrderBy(r => r.Role).ThenBy(r => r.FullName).ThenBy(r => r.UserId)
            .Skip((page - 1) * size).Take(size).ToListAsync(ct);

        return new PagedResult<ProjectMemberDto>(rows.Select(ToDto).ToList(), page, size, total);
    }

    public async Task<ProjectMemberDto?> GetMemberAsync(Guid projectId, Guid userId, CancellationToken ct) =>
        await MemberRows(projectId).Where(r => r.UserId == userId).FirstOrDefaultAsync(ct) is { } row ? ToDto(row) : null;

    private sealed class MemberRow
    {
        public Guid UserId { get; init; }
        public ProjectRole Role { get; init; }
        public DateTimeOffset JoinedAt { get; init; }
        public string? FullName { get; init; }
        public string? Email { get; init; }
        public bool? IsActive { get; init; }
    }

    private IQueryable<MemberRow> MemberRows(Guid projectId) =>
        from m in db.ProjectMembers.AsNoTracking()
        join p in db.Projects.AsNoTracking() on m.ProjectId equals p.Id
        join u in db.UserRefs.AsNoTracking() on m.UserId equals u.UserId into known
        from u in known.DefaultIfEmpty()
        where p.PublicId == projectId
        select new MemberRow
        {
            UserId = m.UserId,
            Role = m.Role,
            JoinedAt = m.JoinedAt,
            FullName = u != null ? u.FullName : null,
            Email = u != null ? u.Email : null,
            IsActive = u != null ? (bool?)u.IsActive : null,
        };

    private static ProjectMemberDto ToDto(MemberRow r) =>
        new(r.UserId, r.FullName, r.Email, ProjectRoleNames.ToApi(r.Role), r.JoinedAt, r.IsActive);
}

public sealed class UserDirectory(ProjectDbContext db) : IUserDirectory
{
    public async Task<bool?> IsActiveAsync(Guid userId, CancellationToken ct)
    {
        var states = await db.UserRefs.AsNoTracking().Where(u => u.UserId == userId).Select(u => u.IsActive).ToListAsync(ct);
        return states.Count == 0 ? null : states[0];
    }
}

public sealed class MilestoneRepository(ProjectDbContext db) : IMilestoneRepository
{
    public Task<Milestone?> FindAsync(long projectId, Guid milestoneId, CancellationToken ct) =>
        db.Milestones.FirstOrDefaultAsync(m => m.ProjectId == projectId && m.PublicId == milestoneId, ct);

    public async Task<(DateOnly? Earliest, DateOnly? Latest)> GetDueDateRangeAsync(long projectId, CancellationToken ct)
    {
        // Jumlah milestone satu proyek kecil; min/max dihitung di memori agar tidak bergantung pada terjemahan konversi tanggal.
        var dates = await db.Milestones.AsNoTracking().Where(m => m.ProjectId == projectId).Select(m => m.DueDate).ToListAsync(ct);
        return dates.Count == 0 ? (null, null) : (dates.Min(), dates.Max());
    }

    public async Task AddAsync(Milestone milestone, CancellationToken ct) => await db.Milestones.AddAsync(milestone, ct);
}

public sealed class MilestoneReadService(ProjectDbContext db) : IMilestoneReadService
{
    public async Task<PagedResult<MilestoneDto>> ListAsync(Guid projectId, int page, int size, CancellationToken ct)
    {
        var query = from m in db.Milestones.AsNoTracking()
                    join p in db.Projects.AsNoTracking() on m.ProjectId equals p.Id
                    where p.PublicId == projectId
                    select m;

        var total = await query.LongCountAsync(ct);
        var items = await query.OrderBy(m => m.DueDate).ThenBy(m => m.Name).ThenBy(m => m.Id)
            .Skip((page - 1) * size).Take(size).ToListAsync(ct);

        return new PagedResult<MilestoneDto>(items.Select(m => MilestoneDto.From(m, projectId)).ToList(), page, size, total);
    }
}
