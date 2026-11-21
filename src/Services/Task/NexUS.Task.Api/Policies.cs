namespace NexUS.Tasks.Api;

/// <summary>
/// Nama policy endpoint Task Service. Setiap nama berformat <c>modul.aksi</c> dibuat otomatis oleh
/// <c>PermissionPolicyProvider</c> dan diperiksa terhadap permission user (NX-020, ADR-0009); keanggotaan proyek
/// diperiksa di use case (<c>TaskGuard</c>).
/// </summary>
public static class Policies
{
    public const string TaskRead = "task.read";
    public const string TaskCreate = "task.create";
    public const string TaskUpdate = "task.update";
    public const string TaskDelete = "task.delete";
}
