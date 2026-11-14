using NexUS.BuildingBlocks.Auth;
using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.Project.Application.Abstractions;

namespace NexUS.Project.Infrastructure.Security;

/// <summary>
/// Memeriksa scope penugasan role memakai permission user dari User Service (dengan cache, NX-020). Permission dibaca
/// sekali per request (service ini scoped) walau diperiksa beberapa kali.
/// </summary>
public sealed class ProjectAccess(IPermissionService permissions, ICurrentUser currentUser) : IProjectAccess
{
    private IReadOnlyList<GrantedPermission>? _granted;

    private async Task<IReadOnlyList<GrantedPermission>> GrantedAsync(CancellationToken ct) =>
        _granted ??= await permissions.GetAsync(currentUser.TenantId, currentUser.UserId, ct);

    public async Task EnsureAsync(string permission, Guid? projectId, Guid? departmentId, CancellationToken ct)
    {
        var allowed = (await GrantedAsync(ct)).Any(g => g.Code == permission && g.ScopeType.ToLowerInvariant() switch
        {
            "global" => true,
            "project" => projectId is not null && g.ScopeId == projectId,
            "department" => departmentId is not null && g.ScopeId == departmentId,
            _ => false,
        });

        if (!allowed) throw new ForbiddenException("Izin Anda tidak mencakup proyek atau departemen ini.");
    }

    public Task<bool> CanSeeAllAsync(CancellationToken ct) => HoldsGloballyAsync(ProjectPermissions.ReadAll, ct);

    public async Task<bool> HoldsGloballyAsync(string permission, CancellationToken ct) =>
        (await GrantedAsync(ct)).Any(g =>
            g.Code == permission && string.Equals(g.ScopeType, "global", StringComparison.OrdinalIgnoreCase));
}
