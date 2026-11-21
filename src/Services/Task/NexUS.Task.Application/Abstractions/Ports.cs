using NexUS.BuildingBlocks.Common.Results;
using NexUS.Tasks.Application.Tasks;
using NexUS.Tasks.Domain.Tasks;

namespace NexUS.Tasks.Application.Abstractions;

// Semua port otomatis dibatasi ke tenant user yang sedang login (CONTEXT.md bagian 3 poin 8):
// data tenant lain dianggap tidak ada.

public interface ITaskRepository
{
    Task<TaskItem?> FindAsync(Guid publicId, CancellationToken ct);

    /// <summary>Subtask aktif dari tugas <paramref name="parentId"/> (ID internal).</summary>
    Task<IReadOnlyList<TaskItem>> FindSubtasksAsync(long parentId, CancellationToken ct);

    Task AddAsync(TaskItem task, CancellationToken ct);
}

public interface ITaskReadService
{
    Task<TaskDto?> GetAsync(Guid publicId, CancellationToken ct);

    Task<PagedResult<TaskDto>> ListAsync(TaskFilter filter, CancellationToken ct);
}

/// <param name="MemberUserId">Bila diisi, hanya tugas di proyek yang diikuti user ini.</param>
/// <param name="DueBefore">Jatuh tempo pada atau sebelum tanggal ini.</param>
public sealed record TaskFilter(
    Guid? ProjectId, Guid? AssigneeId, TaskItemStatus? Status, DateOnly? DueBefore, Guid? MemberUserId, int Page, int Size);

/// <summary>Proyek seperti yang dikenal Task Service (read model dari event Project Service).</summary>
public sealed record ProjectInfo(Guid ProjectId, bool IsArchived);

/// <summary>Read model proyek dan anggotanya (<c>PROJECT_REF</c>, <c>PROJECT_MEMBER_REF</c>).</summary>
public interface IProjectDirectory
{
    /// <summary>Null bila proyek belum dikenal (event-nya belum tiba) atau tidak ada.</summary>
    Task<ProjectInfo?> FindProjectAsync(Guid projectId, CancellationToken ct);

    /// <summary>Peran aktif user di proyek (<c>MANAGER</c>/<c>MEMBER</c>/<c>VIEWER</c>); null bila bukan anggota.</summary>
    Task<string?> GetMemberRoleAsync(Guid projectId, Guid userId, CancellationToken ct);
}

/// <summary>Kode permission yang diperiksa scope-nya (sama dengan nama policy di Api).</summary>
public static class TaskPermissions
{
    public const string Create = "task.create";
    public const string Update = "task.update";
    public const string Delete = "task.delete";

    /// <summary>Melihat semua proyek (dan tugasnya) tanpa menjadi anggota (admin tenant, auditor).</summary>
    public const string ReadAllProjects = "project.all.read";
}

public interface ITaskAccess
{
    /// <summary>User memegang <paramref name="permission"/> dengan scope global.</summary>
    Task<bool> HoldsGloballyAsync(string permission, CancellationToken ct);
}
