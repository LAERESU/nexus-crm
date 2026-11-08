using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;

namespace NexUS.User.Domain.Tenants;

/// <summary>Perusahaan pelanggan. Tenant adalah akar isolasi data, jadi <see cref="Entity.TenantId"/>-nya tidak dipakai.</summary>
public sealed class Tenant : AggregateRoot
{
    public const int CodeMaxLength = 50;
    public const int NameMaxLength = 200;

    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public bool IsActive { get; private set; } = true;

    private Tenant() { } // untuk EF Core

    public static Tenant Create(string code, string name)
    {
        var tenant = new Tenant { Code = NormalizeCode(code) };
        tenant.Rename(name);
        return tenant;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Nama tenant wajib diisi.");
        if (name.Trim().Length > NameMaxLength) throw new DomainException($"Nama tenant maksimal {NameMaxLength} karakter.");
        Name = name.Trim();
    }

    public static string NormalizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new DomainException("Kode tenant wajib diisi.");
        var normalized = code.Trim().ToLowerInvariant();
        if (normalized.Length > CodeMaxLength) throw new DomainException($"Kode tenant maksimal {CodeMaxLength} karakter.");
        return normalized;
    }
}
