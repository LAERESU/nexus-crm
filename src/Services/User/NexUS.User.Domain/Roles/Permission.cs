using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;

namespace NexUS.User.Domain.Roles;

/// <summary>Permission berformat <c>modul.aksi</c>. Katalog bersifat global (bukan per tenant).</summary>
public sealed class Permission : Entity
{
    public const int CodeMaxLength = 100;
    public const int DescriptionMaxLength = 500;

    public string Code { get; private set; } = default!;
    public string Module { get; private set; } = default!;
    public string? Description { get; private set; }

    private Permission() { } // untuk EF Core

    public static Permission Create(string code, string? description)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new DomainException("Kode permission wajib diisi.");
        var normalized = code.Trim().ToLowerInvariant();
        var dot = normalized.IndexOf('.', StringComparison.Ordinal);
        if (normalized.Length > CodeMaxLength || dot <= 0 || dot == normalized.Length - 1)
            throw new DomainException("Kode permission harus berformat modul.aksi.");

        return new Permission { Code = normalized, Module = normalized[..dot], Description = description };
    }
}
