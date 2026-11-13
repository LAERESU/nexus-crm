using NexUS.BuildingBlocks.Auth;
using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.Project.Application.Abstractions;

namespace NexUS.Project.Infrastructure.Security;

/// <summary>Memeriksa scope penugasan role memakai permission user dari User Service (dengan cache, NX-020).</summary>
public sealed class ProjectAccess(IPermissionService permissions, ICurrentUser currentUser) : IProjectAccess
{
    public async Task EnsureAsync(string permission, Guid? projectId, Guid? departmentId, CancellationToken ct)
    {
        var granted = await permissions.GetAsync(currentUser.TenantId, currentUser.UserId, ct);

        var allowed = granted.Any(g => g.Code == permission && g.ScopeType.ToLowerInvariant() switch
        {
            "global" => true,
            "project" => projectId is not null && g.ScopeId == projectId,
            "department" => departmentId is not null && g.ScopeId == departmentId,
            _ => false,
        });

        if (!allowed) throw new ForbiddenException("Izin Anda tidak mencakup proyek atau departemen ini.");
    }
}
