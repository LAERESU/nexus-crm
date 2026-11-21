using Microsoft.EntityFrameworkCore;
using NexUS.BuildingBlocks.Common.Results;
using NexUS.Tasks.Application.Abstractions;
using NexUS.Tasks.Application.Tasks;
using NexUS.Tasks.Domain.Tasks;

namespace NexUS.Tasks.Infrastructure.Persistence;

// Filter TENANT_ID dipasang sebagai global query filter di TaskDbContext, sehingga semua query di sini otomatis
// dibatasi ke tenant user yang sedang login.

public sealed class TaskRepository(TaskDbContext db) : ITaskRepository
{
    public Task<TaskItem?> FindAsync(Guid publicId, CancellationToken ct) =>
        db.Tasks.FirstOrDefaultAsync(t => t.PublicId == publicId, ct);

    public async Task<IReadOnlyList<TaskItem>> FindSubtasksAsync(long parentId, CancellationToken ct) =>
        await db.Tasks.Where(t => t.ParentTaskId == parentId).ToListAsync(ct);

    public async Task AddAsync(TaskItem task, CancellationToken ct) => await db.Tasks.AddAsync(task, ct);
}

public sealed class TaskReadService(TaskDbContext db) : ITaskReadService
{
    public async Task<TaskDto?> GetAsync(Guid publicId, CancellationToken ct) =>
        (await ToDtosAsync(db.Tasks.AsNoTracking().Where(t => t.PublicId == publicId), ct)).FirstOrDefault();

    public async Task<PagedResult<TaskDto>> ListAsync(TaskFilter filter, CancellationToken ct)
    {
        var query = ListQuery(filter);
        var total = await query.LongCountAsync(ct);
        var page = query
            .OrderBy(t => t.DueDate) // Oracle: NULL di akhir untuk urutan naik
            .ThenBy(t => t.Id)
            .Skip((filter.Page - 1) * filter.Size)
            .Take(filter.Size);

        return new PagedResult<TaskDto>(await ToDtosAsync(page, ct), filter.Page, filter.Size, total);
    }

    /// <summary>
    /// Query daftar tugas (tanpa urutan dan paging). Publik agar tes bisa memeriksa rencana eksekusinya: filter proyek dan
    /// status memakai <c>IX_TASKS_TNT_PRJ_STATUS</c>.
    /// </summary>
    public IQueryable<TaskItem> ListQuery(TaskFilter filter)
    {
        var query = db.Tasks.AsNoTracking();

        if (filter.ProjectId is { } projectId) query = query.Where(t => t.ProjectId == projectId);
        if (filter.Status is { } status) query = query.Where(t => t.Status == status);
        if (filter.DueBefore is { } dueBefore) query = query.Where(t => t.DueDate != null && t.DueDate <= dueBefore);
        if (filter.AssigneeId is { } assigneeId)
            query = query.Where(t => db.TaskAssignees.Any(a => a.TaskId == t.Id && a.UserId == assigneeId));
        if (filter.MemberUserId is { } memberId)
            query = query.Where(t => db.ProjectMemberRefs.Any(m => m.ProjectId == t.ProjectId && m.UserId == memberId && !m.IsRemoved));

        return query;
    }

    private async Task<IReadOnlyList<TaskDto>> ToDtosAsync(IQueryable<TaskItem> tasks, CancellationToken ct)
    {
        var rows = await tasks
            .Select(t => new
            {
                Task = t,
                ParentId = db.Tasks.Where(p => p.Id == t.ParentTaskId).Select(p => (Guid?)p.PublicId).FirstOrDefault(),
            })
            .ToListAsync(ct);
        if (rows.Count == 0) return [];

        var ids = rows.Select(r => r.Task.Id).ToList();
        var assignees = (await db.TaskAssignees.AsNoTracking().Where(a => ids.Contains(a.TaskId)).Select(a => new { a.TaskId, a.UserId }).ToListAsync(ct))
            .ToLookup(a => a.TaskId, a => a.UserId);

        return rows.Select(r => TaskDto.From(r.Task, r.ParentId, assignees[r.Task.Id].ToList())).ToList();
    }
}

public sealed class ProjectDirectory(TaskDbContext db) : IProjectDirectory
{
    public async Task<ProjectInfo?> FindProjectAsync(Guid projectId, CancellationToken ct)
    {
        var rows = await db.ProjectRefs.AsNoTracking().Where(p => p.ProjectId == projectId)
            .Select(p => new ProjectInfo(p.ProjectId, p.IsArchived)).Take(1).ToListAsync(ct);
        return rows.Count == 0 ? null : rows[0];
    }

    public async Task<string?> GetMemberRoleAsync(Guid projectId, Guid userId, CancellationToken ct)
    {
        var roles = await db.ProjectMemberRefs.AsNoTracking()
            .Where(m => m.ProjectId == projectId && m.UserId == userId && !m.IsRemoved)
            .Select(m => m.Role).Take(1).ToListAsync(ct);
        return roles.Count == 0 ? null : roles[0];
    }
}
