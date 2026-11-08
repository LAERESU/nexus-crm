using NexUS.User.Domain.Users;

namespace NexUS.User.Application.Roles;

/// <param name="Permissions">Kode permission (<c>modul.aksi</c>) milik role, terurut.</param>
/// <param name="RowVersion">Dikirim balik klien lewat header <c>If-Match</c>.</param>
public sealed record RoleDto(
    Guid Id, string Code, string Name, string? Description, bool IsSystem, IReadOnlyList<string> Permissions, long RowVersion);

public sealed record PermissionDto(string Code, string Module, string? Description);

public sealed record UserRoleDto(Guid RoleId, string RoleCode, string ScopeType, Guid? ScopeId);

public sealed record GrantedPermissionDto(string Code, string ScopeType, Guid? ScopeId);

public sealed record UserPermissionsDto(Guid UserId, IReadOnlyList<UserRoleDto> Roles, IReadOnlyList<GrantedPermissionDto> Permissions);

/// <summary>Nama scope di API (huruf kecil) dan kolom SCOPE_TYPE (huruf besar).</summary>
public static class ScopeTypeNames
{
    public static bool TryParse(string value, out RoleScopeType scopeType)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "global": scopeType = RoleScopeType.Global; return true;
            case "department": scopeType = RoleScopeType.Department; return true;
            case "project": scopeType = RoleScopeType.Project; return true;
            default: scopeType = default; return false;
        }
    }

    public static string ToApi(RoleScopeType scopeType) => scopeType.ToString().ToLowerInvariant();
}
