using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;

namespace NexUS.User.Domain.Departments;

/// <summary>Departemen dalam satu tenant; membentuk hierarki lewat <see cref="ParentId"/>.</summary>
public sealed class Department : AggregateRoot
{
    public const int CodeMaxLength = 50;
    public const int NameMaxLength = 200;

    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;

    /// <summary>ID internal induk; null untuk departemen tingkat atas.</summary>
    public long? ParentId { get; private set; }

    private Department() { } // untuk EF Core

    public static Department Create(long tenantId, string code, string name, long? parentId)
    {
        if (tenantId <= 0) throw new DomainException("Tenant wajib diisi.");

        var department = new Department { TenantId = tenantId, Code = NormalizeCode(code), ParentId = parentId };
        department.Rename(name);
        return department;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Nama departemen wajib diisi.");
        if (name.Trim().Length > NameMaxLength) throw new DomainException($"Nama departemen maksimal {NameMaxLength} karakter.");
        Name = name.Trim();
    }

    /// <summary>
    /// Memindahkan departemen ke induk lain. <paramref name="ancestorIds"/> adalah ID internal seluruh leluhur calon
    /// induk (termasuk calon induk itu sendiri); bila memuat departemen ini, perpindahan akan membentuk siklus.
    /// </summary>
    public void MoveTo(long? newParentId, IReadOnlyCollection<long> ancestorIds)
    {
        if (newParentId is { } parent && (parent == Id || ancestorIds.Contains(Id)))
            throw new DomainException("Departemen tidak boleh menjadi turunan dirinya sendiri.");

        ParentId = newParentId;
    }

    public static string NormalizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new DomainException("Kode departemen wajib diisi.");
        var normalized = code.Trim().ToUpperInvariant();
        if (normalized.Length > CodeMaxLength) throw new DomainException($"Kode departemen maksimal {CodeMaxLength} karakter.");
        return normalized;
    }
}
