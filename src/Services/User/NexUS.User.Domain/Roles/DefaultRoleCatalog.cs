namespace NexUS.User.Domain.Roles;

public sealed record PermissionDefinition(string Code, string Description);

public sealed record RoleDefinition(string Code, string Name, string Description, IReadOnlyList<string> Permissions);

/// <summary>
/// Satu-satunya sumber katalog permission dan role bawaan (ARCHITECTURE.md G.2). Seeder menyamakan database dengan
/// katalog ini, sehingga menambah permission cukup dengan menambahkannya di sini.
/// </summary>
public static class DefaultRoleCatalog
{
    public static readonly IReadOnlyList<PermissionDefinition> Permissions =
    [
        new("user.read", "Melihat daftar dan detail user serta departemen"),
        new("user.manage", "Mengelola user, departemen, dan tenant"),
        new("role.read", "Melihat role dan permission"),
        new("role.manage", "Mengelola role dan penugasan role ke user"),
        new("project.read", "Melihat proyek"),
        new("project.create", "Membuat proyek"),
        new("project.update", "Mengubah proyek"),
        new("project.member.manage", "Mengelola anggota proyek"),
        new("task.read", "Melihat tugas"),
        new("task.create", "Membuat tugas"),
        new("task.update", "Mengubah tugas"),
        new("task.delete", "Menghapus tugas"),
        new("sop.read", "Membaca SOP yang terbit"),
        new("sop.create", "Membuat SOP"),
        new("sop.edit", "Mengedit draf SOP"),
        new("sop.submit", "Mengajukan SOP untuk review"),
        new("sop.review", "Mereview SOP"),
        new("sop.approve", "Menyetujui SOP"),
        new("sop.publish", "Menerbitkan SOP"),
        new("sop.category.manage", "Mengelola kategori SOP"),
        new("audit.read", "Melihat audit log"),
        new("dashboard.read", "Melihat dashboard"),
    ];

    /// <summary>Permission baca: dimiliki TENANT_ADMIN dan AUDITOR (read-only).</summary>
    private static readonly string[] ReadPermissions =
    [
        "user.read", "role.read", "project.read", "task.read", "sop.read", "audit.read", "dashboard.read",
    ];

    // SUPER_ADMIN (platform, "kelola tenant") sengaja tidak ada: ia bukan role per tenant dan belum ada permission-nya.
    public static readonly IReadOnlyList<RoleDefinition> Roles =
    [
        new("TENANT_ADMIN", "Admin Tenant", "Mengelola user, role, dan kategori SOP; membaca semua modul",
            [.. ReadPermissions, "user.manage", "role.manage", "sop.category.manage"]),
        new("PROJECT_MANAGER", "Manajer Proyek", "Mengelola proyek, anggota, dan tugas",
            ["project.read", "project.create", "project.update", "project.member.manage",
             "task.read", "task.create", "task.update", "task.delete", "sop.read", "dashboard.read"]),
        new("MEMBER", "Anggota", "Bekerja pada proyek dan tugas, membaca SOP",
            ["user.read", "project.read", "task.read", "task.create", "task.update", "sop.read", "dashboard.read"]),
        new("SOP_AUTHOR", "Penulis SOP", "Membuat, mengedit, dan mengajukan SOP",
            ["sop.read", "sop.create", "sop.edit", "sop.submit"]),
        new("SOP_REVIEWER", "Reviewer SOP", "Mereview SOP", ["sop.read", "sop.review"]),
        new("SOP_APPROVER", "Approver SOP", "Menyetujui dan menerbitkan SOP", ["sop.read", "sop.approve", "sop.publish"]),
        new("AUDITOR", "Auditor", "Hanya membaca, termasuk audit log", ReadPermissions),
    ];
}
