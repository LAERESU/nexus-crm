using Microsoft.AspNetCore.Authorization;
using NexUS.BuildingBlocks.Auth;

namespace NexUS.User.Api;

/// <summary>
/// Nama policy endpoint User Service. Selain <see cref="Authenticated"/>, setiap nama berformat <c>modul.aksi</c> dibuat
/// otomatis oleh <c>PermissionPolicyProvider</c> dan diperiksa terhadap permission user (NX-020, ADR-0009); konstanta
/// di sini hanya agar bisa dipakai di atribut (<c>const</c>).
/// </summary>
public static class Policies
{
    public const string Authenticated = "authenticated";
    public const string UserRead = "user.read";
    public const string UserManage = "user.manage";
    public const string RoleRead = "role.read";
    public const string RoleManage = "role.manage";

    public static void Register(AuthorizationOptions options)
    {
        options.AddPolicy(Authenticated, policy => policy.RequireAuthenticatedUser());

        // Endpoint kelola berlaku di seluruh tenant: penugasan role bersope departemen/proyek tidak cukup.
        foreach (var permission in new[] { UserManage, RoleManage })
            options.AddPolicy(permission, policy => policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission, globalOnly: true)));
    }
}
