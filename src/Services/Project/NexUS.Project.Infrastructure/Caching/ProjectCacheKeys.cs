using NexUS.BuildingBlocks.Caching;

namespace NexUS.Project.Infrastructure.Caching;

/// <summary>
/// Kunci cache Project Service (ARCHITECTURE.md B.3), tanpa <c>Cache:KeyPrefix</c> agar service lain (Task Service)
/// bisa membaca kunci yang sama. Nilainya dihapus setiap kali proyek atau anggotanya berubah.
/// </summary>
public static class ProjectCacheKeys
{
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    /// <summary>Detail proyek (<c>ProjectDto</c>).</summary>
    public static string Project(long tenantId, Guid projectId) => CacheKeys.Tenant(tenantId, $"project:{projectId:D}");

    /// <summary>Keanggotaan proyek (user dan perannya), dipakai pemeriksaan akses.</summary>
    public static string Members(long tenantId, Guid projectId) => CacheKeys.Tenant(tenantId, $"project:{projectId:D}:members");
}

/// <summary>Satu anggota di cache keanggotaan; peran memakai nama kolom (<c>MANAGER</c>, ...).</summary>
public sealed record CachedMember(Guid UserId, string Role);
