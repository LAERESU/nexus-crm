using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;

namespace NexUS.BuildingBlocks.Auth;

/// <summary>
/// Permission user yang sedang login beserta scope-nya, dibaca sekali per request (layanan scoped). Policy endpoint hanya
/// memastikan permission dipegang pada scope apa pun (<see cref="PermissionHandler"/>); service pemilik data memakai kelas
/// ini untuk memastikan scope-nya mencakup objek yang disentuh.
/// </summary>
public sealed class CurrentUserGrants(IPermissionService permissions, ICurrentUser currentUser)
{
    private IReadOnlyList<GrantedPermission>? _granted;

    public async Task<IReadOnlyList<GrantedPermission>> GetAsync(CancellationToken ct) =>
        _granted ??= await permissions.GetAsync(currentUser.TenantId, currentUser.UserId, ct);

    /// <summary>Permission dipegang dengan scope global.</summary>
    public async Task<bool> HoldsGloballyAsync(string permission, CancellationToken ct) =>
        (await GetAsync(ct)).Any(g => g.Code == permission && IsScope(g, "global"));

    /// <summary>Permission dipegang secara global, untuk proyek ini, atau untuk departemennya (bila diketahui).</summary>
    public async Task<bool> CoversAsync(string permission, Guid? projectId, Guid? departmentId, CancellationToken ct) =>
        (await GetAsync(ct)).Any(g => g.Code == permission && (
            IsScope(g, "global")
            || (IsScope(g, "project") && projectId is not null && g.ScopeId == projectId)
            || (IsScope(g, "department") && departmentId is not null && g.ScopeId == departmentId)));

    /// <summary>Melempar <c>ForbiddenException</c> (403) bila <see cref="CoversAsync"/> false.</summary>
    public async Task EnsureCoversAsync(string permission, Guid? projectId, Guid? departmentId, CancellationToken ct)
    {
        if (!await CoversAsync(permission, projectId, departmentId, ct))
            throw new ForbiddenException("Izin Anda tidak mencakup proyek atau departemen ini.");
    }

    private static bool IsScope(GrantedPermission grant, string scope) =>
        string.Equals(grant.ScopeType, scope, StringComparison.OrdinalIgnoreCase);
}
