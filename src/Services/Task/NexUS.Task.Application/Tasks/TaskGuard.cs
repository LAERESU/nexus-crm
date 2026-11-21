using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.Tasks.Application.Abstractions;
using NexUS.Tasks.Domain.Projects;

namespace NexUS.Tasks.Application.Tasks;

/// <summary>
/// Kepemilikan objek (CONTEXT.md bagian 8): tugas sebuah proyek hanya bisa dilihat anggota proyek itu dan hanya bisa
/// dibuat/diubah/dihapus anggota yang bukan pengamat, dengan permission tulis yang scope-nya mencakup proyek itu.
/// Pemegang <c>project.all.read</c> (admin tenant, auditor) boleh melihat semua; menulis tanpa menjadi anggota hanya bila
/// permission tulisnya juga dipegang secara global (ADR-0011).
/// </summary>
public sealed class TaskGuard(IProjectDirectory projects, ITaskAccess access, ICurrentUser currentUser)
{
    private bool? _seesAll;

    private async Task<bool> SeesAllAsync(CancellationToken ct) =>
        _seesAll ??= await access.HoldsGloballyAsync(TaskPermissions.ReadAllProjects, ct);

    /// <summary>Proyek harus dikenal (404) dan user anggotanya (403).</summary>
    public async Task<ProjectInfo> EnsureCanReadAsync(Guid projectId, CancellationToken ct)
    {
        var project = await projects.FindProjectAsync(projectId, ct) ?? throw new NotFoundException("Proyek tidak ditemukan.");
        if (await SeesAllAsync(ct)) return project;
        if (await projects.GetMemberRoleAsync(projectId, currentUser.UserId, ct) is null)
            throw new ForbiddenException("Anda bukan anggota proyek ini.");
        return project;
    }

    /// <summary>
    /// Proyek dikenal (404); permission <paramref name="permission"/> mencakup proyek ini dan user anggota bukan pengamat
    /// (403); baru setelah itu proyek tidak diarsipkan (422), agar bukan anggota tidak mengetahui status arsip.
    /// </summary>
    public async Task EnsureCanWriteAsync(Guid projectId, string permission, CancellationToken ct)
    {
        var project = await projects.FindProjectAsync(projectId, ct) ?? throw new NotFoundException("Proyek tidak ditemukan.");

        if (!(await SeesAllAsync(ct) && await access.HoldsGloballyAsync(permission, ct)))
        {
            var role = await projects.GetMemberRoleAsync(projectId, currentUser.UserId, ct);
            if (role is null) throw new ForbiddenException("Anda bukan anggota proyek ini.");
            if (role == ProjectMemberRef.Viewer) throw new ForbiddenException("Pengamat proyek hanya bisa melihat tugas.");
            if (!await access.CoversProjectAsync(permission, projectId, ct))
                throw new ForbiddenException("Izin Anda tidak mencakup proyek ini.");
        }

        if (project.IsArchived) throw new DomainException("Proyek sudah diarsipkan; tugasnya hanya bisa dilihat.");
    }

    /// <summary>User yang menjadi batas daftar tugas tanpa filter proyek: null bila boleh melihat semua proyek.</summary>
    public async Task<Guid?> ListScopeAsync(CancellationToken ct) => await SeesAllAsync(ct) ? null : currentUser.UserId;
}
