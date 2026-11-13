namespace NexUS.Project.Api;

/// <summary>
/// Nama policy endpoint Project Service. Setiap nama berformat <c>modul.aksi</c> dibuat otomatis oleh
/// <c>PermissionPolicyProvider</c> dan diperiksa terhadap permission user (NX-020, ADR-0009); konstanta di sini hanya
/// agar bisa dipakai di atribut (<c>const</c>). Keanggotaan proyek diperiksa di use case (<c>ProjectGuard</c>).
/// </summary>
public static class Policies
{
    public const string ProjectRead = "project.read";
    public const string ProjectCreate = "project.create";
    public const string ProjectUpdate = "project.update";
    public const string ProjectMemberManage = "project.member.manage";
}
