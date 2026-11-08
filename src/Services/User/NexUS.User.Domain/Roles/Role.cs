using System.Text.RegularExpressions;
using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;

namespace NexUS.User.Domain.Roles;

/// <summary>Role dalam satu tenant. Role sistem (<see cref="IsSystem"/>) tidak dapat diubah atau dihapus lewat API.</summary>
public sealed partial class Role : AggregateRoot
{
    public const int CodeMaxLength = 50;
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 500;

    private readonly List<RolePermission> _permissions = [];

    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public bool IsSystem { get; private set; }

    public IReadOnlyCollection<RolePermission> Permissions => _permissions;

    private Role() { } // untuk EF Core

    public static Role Create(
        long tenantId, string code, string name, string? description, IReadOnlyCollection<Permission> permissions, bool isSystem = false)
    {
        if (tenantId <= 0) throw new DomainException("Tenant wajib diisi.");

        var role = new Role { TenantId = tenantId, Code = NormalizeCode(code), IsSystem = isSystem };
        role.ApplyDetails(name, description);
        foreach (var permission in permissions.DistinctBy(p => p.Id))
            role._permissions.Add(RolePermission.Create(tenantId, permission.Id));
        return role;
    }

    public void Update(string name, string? description)
    {
        EnsureNotSystem();
        ApplyDetails(name, description);
        Touch();
    }

    /// <summary>Mengganti seluruh permission role. Event <see cref="RoleChangedDomainEvent"/> hanya bila ada perubahan.</summary>
    public void SetPermissions(IReadOnlyCollection<Permission> desired)
    {
        EnsureNotSystem();
        ApplyPermissions(desired);
    }

    /// <summary>Menyamakan permission role sistem dengan katalog bawaan (dipakai seeder, bukan API).</summary>
    public void SyncSystemPermissions(IReadOnlyCollection<Permission> desired)
    {
        if (!IsSystem) throw new DomainException("Hanya role sistem yang disinkronkan dari katalog.");
        ApplyPermissions(desired);
    }

    public void Delete()
    {
        EnsureNotSystem();
        MarkDeleted();
    }

    public static string NormalizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new DomainException("Kode role wajib diisi.");
        var normalized = code.Trim().ToUpperInvariant();
        if (normalized.Length > CodeMaxLength || !CodePattern().IsMatch(normalized))
            throw new DomainException($"Kode role harus HURUF_BESAR_DAN_GARIS_BAWAH (maksimal {CodeMaxLength} karakter).");
        return normalized;
    }

    private void ApplyPermissions(IReadOnlyCollection<Permission> desired)
    {
        var desiredIds = desired.Select(p => p.Id).ToHashSet();
        var removed = _permissions.Where(p => !desiredIds.Contains(p.PermissionId)).ToList();
        var added = desiredIds.Where(id => _permissions.All(p => p.PermissionId != id)).ToList();
        if (removed.Count == 0 && added.Count == 0) return;

        foreach (var permission in removed) _permissions.Remove(permission);
        foreach (var id in added) _permissions.Add(RolePermission.Create(TenantId, id));

        Touch();
        // Consumer (NX-020) menghapus cache permission semua user yang memegang role ini.
        AddDomainEvent(new RoleChangedDomainEvent(TenantId, PublicId, UserId: null, RoleChangeReason.RolePermissionsChanged));
    }

    private void ApplyDetails(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Nama role wajib diisi.");
        if (name.Trim().Length > NameMaxLength) throw new DomainException($"Nama role maksimal {NameMaxLength} karakter.");
        if (description is { Length: > DescriptionMaxLength })
            throw new DomainException($"Deskripsi role maksimal {DescriptionMaxLength} karakter.");

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    private void EnsureNotSystem()
    {
        if (IsSystem) throw new DomainException("Role sistem tidak dapat diubah atau dihapus.");
    }

    // Perubahan pada baris anak saja tidak menaikkan ROW_VERSION agregat; menandai UpdatedAt membuat baris role
    // ikut di-UPDATE (nilainya ditimpa saat SaveChanges), sehingga If-Match juga melindungi perubahan permission.
    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

    [GeneratedRegex("^[A-Z][A-Z0-9_]*$")]
    private static partial Regex CodePattern();
}

/// <summary>Baris penghubung role dan permission (ID internal keduanya).</summary>
public sealed class RolePermission : Entity
{
    public long RoleId { get; private set; }
    public long PermissionId { get; private set; }

    private RolePermission() { } // untuk EF Core

    internal static RolePermission Create(long tenantId, long permissionId) =>
        new() { TenantId = tenantId, PermissionId = permissionId };
}

public enum RoleChangeReason
{
    RolePermissionsChanged,
    UserRolesChanged,
}

/// <summary>
/// Diterbitkan bila akses efektif berubah. <paramref name="UserId"/> berisi bila satu user terdampak; kosong bila
/// seluruh pemegang <paramref name="RoleId"/> terdampak.
/// </summary>
public sealed record RoleChangedDomainEvent(long TenantId, Guid? RoleId, Guid? UserId, RoleChangeReason Reason) : DomainEvent;
