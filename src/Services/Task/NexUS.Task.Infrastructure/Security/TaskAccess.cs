using NexUS.BuildingBlocks.Auth;
using NexUS.BuildingBlocks.Common.Domain;
using NexUS.Tasks.Application.Abstractions;

namespace NexUS.Tasks.Infrastructure.Security;

/// <summary>Permission user dari User Service (dengan cache, NX-020), dibaca sekali per request.</summary>
public sealed class TaskAccess(IPermissionService permissions, ICurrentUser currentUser) : ITaskAccess
{
    private IReadOnlyList<GrantedPermission>? _granted;

    public async Task<bool> HoldsGloballyAsync(string permission, CancellationToken ct)
    {
        _granted ??= await permissions.GetAsync(currentUser.TenantId, currentUser.UserId, ct);
        return _granted.Any(g => g.Code == permission && string.Equals(g.ScopeType, "global", StringComparison.OrdinalIgnoreCase));
    }
}
