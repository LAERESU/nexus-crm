using Microsoft.AspNetCore.Authorization;

namespace NexUS.User.Api;

/// <summary>
/// Policy endpoint User Service. Sampai NX-020 (policy dinamis berbasis permission) selesai, <c>user.manage</c>
/// dipetakan ke role <c>admin</c> dari token (ADR-0006); NX-020 mengganti pemetaan ini dengan pemeriksaan permission.
/// </summary>
public static class Policies
{
    public const string Authenticated = "authenticated";
    public const string UserRead = "user.read"; // = PermissionPolicies.For("user", "read"); const agar bisa dipakai di atribut
    public const string UserManage = "user.manage";

    public static void Register(AuthorizationOptions options)
    {
        options.AddPolicy(Authenticated, policy => policy.RequireAuthenticatedUser());
        options.AddPolicy(UserRead, policy => policy.RequireAuthenticatedUser());
        options.AddPolicy(UserManage, policy => policy.RequireRole("admin"));
    }
}
