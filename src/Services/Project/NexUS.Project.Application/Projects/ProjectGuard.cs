using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.Project.Application.Abstractions;
using NexUS.Project.Domain.Projects;
using ProjectEntity = NexUS.Project.Domain.Projects.Project;

namespace NexUS.Project.Application.Projects;

/// <summary>
/// Kepemilikan objek (CONTEXT.md bagian 8): selain permission, user harus anggota proyek untuk membacanya dan manager
/// proyek untuk mengubahnya. Pengecualian (ADR-0011): pemegang <see cref="ProjectPermissions.ReadAll"/> boleh membaca
/// semua proyek, dan boleh mengubah tanpa menjadi manager hanya bila permission tulisnya juga dipegang secara global.
/// </summary>
public sealed class ProjectGuard(IProjectRepository projects, IProjectAccess access, ICurrentUser currentUser)
{
    public async Task EnsureCanReadAsync(Guid projectId, CancellationToken ct)
    {
        if (await access.CanSeeAllAsync(ct)) return;
        if (await projects.GetMemberRoleAsync(projectId, currentUser.UserId, ct) is null)
            throw new ForbiddenException("Anda bukan anggota proyek ini.");
    }

    /// <summary>
    /// Permission <paramref name="permission"/> harus mencakup proyek ini, dan user harus manager proyek. Anggota dibaca
    /// dari <paramref name="project"/> bila sudah dimuat, selain itu dari database.
    /// </summary>
    public async Task EnsureCanManageAsync(string permission, ProjectEntity project, CancellationToken ct)
    {
        await access.EnsureAsync(permission, project.PublicId, project.DepartmentId, ct);

        // Izin baca saja tidak boleh membuka operasi tulis: admin tenant dikecualikan karena juga memegang permission
        // tulis itu secara global, bukan hanya berscope departemen/proyek.
        if (await access.CanSeeAllAsync(ct) && await access.HoldsGloballyAsync(permission, ct)) return;

        var role = project.FindMember(currentUser.UserId)?.Role
            ?? await projects.GetMemberRoleAsync(project.PublicId, currentUser.UserId, ct);
        if (role != ProjectRole.Manager)
            throw new ForbiddenException("Hanya manager proyek ini yang boleh melakukannya.");
    }

    /// <summary>User yang menjadi batas daftar proyek: null bila boleh melihat semua proyek.</summary>
    public async Task<Guid?> ListScopeAsync(CancellationToken ct) => await access.CanSeeAllAsync(ct) ? null : currentUser.UserId;
}
