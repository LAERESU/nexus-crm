using NexUS.BuildingBlocks.Common.Results;
using NexUS.Project.Application.Projects;
using NexUS.Project.Domain.Projects;
using ProjectEntity = NexUS.Project.Domain.Projects.Project;

namespace NexUS.Project.Application.Abstractions;

// Semua port otomatis dibatasi ke tenant user yang sedang login (CONTEXT.md bagian 3 poin 8):
// data tenant lain dianggap tidak ada.

public interface IProjectRepository
{
    Task<ProjectEntity?> FindAsync(Guid publicId, CancellationToken ct);

    /// <summary>Seperti <see cref="FindAsync"/> dengan anggota dimuat (untuk diubah).</summary>
    Task<ProjectEntity?> FindWithMembersAsync(Guid publicId, CancellationToken ct);

    /// <summary>Peran user di proyek; null bila bukan anggota (atau proyek tidak ada).</summary>
    Task<ProjectRole?> GetMemberRoleAsync(Guid projectId, Guid userId, CancellationToken ct);

    Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken ct);

    Task AddAsync(ProjectEntity project, CancellationToken ct);
}

public interface IProjectReadService
{
    Task<ProjectDto?> GetAsync(Guid publicId, CancellationToken ct);

    Task<PagedResult<ProjectDto>> ListAsync(ProjectFilter filter, CancellationToken ct);

    /// <summary>Anggota proyek beserta nama dari read model user (bila sudah dikenal), urut peran lalu nama.</summary>
    Task<IReadOnlyList<ProjectMemberDto>> ListMembersAsync(Guid projectId, CancellationToken ct);
}

/// <param name="Status">Nama status API; null berarti semua kecuali yang diarsipkan.</param>
/// <param name="MemberUserId">Bila diisi, hanya proyek yang diikuti user ini.</param>
public sealed record ProjectFilter(
    string? Search, string? Status, Guid? OwnerId, Guid? DepartmentId, Guid? MemberUserId, int Page, int Size);

/// <summary>Read model user (USER_REF) yang diisi dari event User Service.</summary>
public interface IUserDirectory
{
    /// <summary>Status aktif user; null bila user belum dikenal (event-nya belum tiba atau lebih tua dari service ini).</summary>
    Task<bool?> IsActiveAsync(Guid userId, CancellationToken ct);
}

/// <summary>Kode permission yang diperiksa terhadap scope penugasan role (sama dengan nama policy di Api).</summary>
public static class ProjectPermissions
{
    public const string Create = "project.create";
    public const string Update = "project.update";
    public const string MemberManage = "project.member.manage";

    /// <summary>Melihat dan mengelola semua proyek tanpa menjadi anggota (admin tenant, auditor).</summary>
    public const string ReadAll = "project.all.read";
}

/// <summary>
/// Pemeriksaan scope atas permission. Policy endpoint hanya memastikan user memegang permission pada scope apa pun; di
/// sini dipastikan scope-nya mencakup objek yang disentuh: global, proyek itu sendiri, atau departemennya.
/// </summary>
public interface IProjectAccess
{
    /// <summary>Melempar <c>ForbiddenException</c> (403) bila tidak ada penugasan yang mencakup proyek/departemen ini.</summary>
    Task EnsureAsync(string permission, Guid? projectId, Guid? departmentId, CancellationToken ct);

    /// <summary>User memegang <see cref="ProjectPermissions.ReadAll"/> secara global.</summary>
    Task<bool> CanSeeAllAsync(CancellationToken ct);
}
