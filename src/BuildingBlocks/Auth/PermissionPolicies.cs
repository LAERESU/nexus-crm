namespace NexUS.BuildingBlocks.Auth;

/// <summary>Nama policy berformat modul.aksi; daftar lengkap diisi di NX-020.</summary>
public static class PermissionPolicies
{
    public static string For(string module, string action) => $"{module}.{action}";
}
