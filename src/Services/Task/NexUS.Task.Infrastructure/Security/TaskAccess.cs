using NexUS.BuildingBlocks.Auth;
using NexUS.Tasks.Application.Abstractions;

namespace NexUS.Tasks.Infrastructure.Security;

/// <summary>Pemeriksaan scope permission lewat <see cref="CurrentUserGrants"/> (dibaca sekali per request).</summary>
public sealed class TaskAccess(CurrentUserGrants grants) : ITaskAccess
{
    public Task<bool> HoldsGloballyAsync(string permission, CancellationToken ct) => grants.HoldsGloballyAsync(permission, ct);

    /// <summary>
    /// Task Service tidak menyimpan departemen proyek, jadi permission berscope departemen tidak bisa dipastikan mencakup
    /// proyek dan dianggap tidak mencakup (fail closed).
    /// </summary>
    public Task<bool> CoversProjectAsync(string permission, Guid projectId, CancellationToken ct) =>
        grants.CoversAsync(permission, projectId, departmentId: null, ct);
}
