using NexUS.BuildingBlocks.Auth;
using NexUS.Project.Application.Abstractions;

namespace NexUS.Project.Infrastructure.Security;

/// <summary>Pemeriksaan scope permission lewat <see cref="CurrentUserGrants"/> (dibaca sekali per request).</summary>
public sealed class ProjectAccess(CurrentUserGrants grants) : IProjectAccess
{
    public Task EnsureAsync(string permission, Guid? projectId, Guid? departmentId, CancellationToken ct) =>
        grants.EnsureCoversAsync(permission, projectId, departmentId, ct);

    public Task<bool> CanSeeAllAsync(CancellationToken ct) => grants.HoldsGloballyAsync(ProjectPermissions.ReadAll, ct);

    public Task<bool> HoldsGloballyAsync(string permission, CancellationToken ct) => grants.HoldsGloballyAsync(permission, ct);
}
