using NexUS.BuildingBlocks.Auth;
using NexUS.BuildingBlocks.Common.Domain;
using NexUS.User.Application.Abstractions;

namespace NexUS.User.Infrastructure.Security;

/// <summary>Sumber permission untuk User Service sendiri: langsung dari database, tanpa HTTP ke dirinya.</summary>
public sealed class DbPermissionSource(IUserPermissionReadService permissions) : IPermissionSource
{
    public async Task<IReadOnlyList<GrantedPermission>> GetAsync(long tenantId, Guid userId, CancellationToken ct = default)
    {
        // Pembacaan sudah dibatasi ke tenant user yang login (filter global); tenantId sama dengan klaim token.
        var result = await permissions.GetAsync(userId, ct);
        return result is null
            ? []
            : result.Permissions.Select(p => new GrantedPermission(p.Code, p.ScopeType, p.ScopeId)).ToList();
    }
}

public sealed class CurrentUserPermissions(IPermissionService permissions, ICurrentUser currentUser) : ICurrentUserPermissions
{
    public async Task<bool> HasAsync(string permission, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId == Guid.Empty) return false;

        var granted = await permissions.GetAsync(currentUser.TenantId, currentUser.UserId, ct);
        return granted.Any(p => p.Code == permission);
    }
}
