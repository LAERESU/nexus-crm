using NexUS.BuildingBlocks.Common.Domain;

namespace NexUS.Tasks.Domain.Projects;

/// <summary>
/// Read model proyek milik Project Service (tabel <c>PROJECT_REF</c>), diperbarui dari event. Event datang lewat antrean
/// terpisah dan bisa tiba tidak berurutan: detail dari event yang lebih lama diabaikan, dan arsip bersifat final.
/// </summary>
public sealed class ProjectRef : Entity
{
    /// <summary>ID publik proyek di Project Service.</summary>
    public Guid ProjectId { get; private set; }

    public string? Code { get; private set; }
    public string? Name { get; private set; }

    /// <summary>Tugas proyek yang diarsipkan menjadi read-only.</summary>
    public bool IsArchived { get; private set; }

    public DateTimeOffset? DetailsEventAt { get; private set; }

    private ProjectRef() { } // untuk EF Core

    public static ProjectRef Create(long tenantId, Guid projectId) => new() { TenantId = tenantId, ProjectId = projectId };

    /// <summary>Menerapkan kode dan nama dari <c>ProjectCreated</c>/<c>ProjectUpdated</c>; false bila event sudah usang.</summary>
    public bool ApplyDetails(string code, string name, DateTimeOffset occurredAt)
    {
        if (DetailsEventAt is { } last && occurredAt < last) return false;

        Code = code;
        Name = name;
        DetailsEventAt = occurredAt;
        return true;
    }

    /// <summary>Arsip tidak bisa dibatalkan (Project Service tidak punya pemulihan), jadi tidak bergantung urutan event.</summary>
    public void Archive() => IsArchived = true;
}

/// <summary>
/// Keanggotaan seorang user di satu proyek (tabel <c>PROJECT_MEMBER_REF</c>). Baris anggota yang dikeluarkan dipertahankan
/// (<see cref="IsRemoved"/>) agar event lama yang tiba belakangan bisa dikenali dan diabaikan.
/// </summary>
public sealed class ProjectMemberRef : Entity
{
    public const string Manager = "MANAGER";
    public const string Member = "MEMBER";
    public const string Viewer = "VIEWER";

    public Guid ProjectId { get; private set; }
    public Guid UserId { get; private set; }

    /// <summary>Peran proyek (huruf besar): <see cref="Manager"/>, <see cref="Member"/>, atau <see cref="Viewer"/>.</summary>
    public string Role { get; private set; } = Member;

    public bool IsRemoved { get; private set; }
    public DateTimeOffset LastEventAt { get; private set; }

    private ProjectMemberRef() { } // untuk EF Core

    public static ProjectMemberRef Create(long tenantId, Guid projectId, Guid userId) =>
        new() { TenantId = tenantId, ProjectId = projectId, UserId = userId, LastEventAt = DateTimeOffset.MinValue };

    /// <summary>Menerapkan peran (event tambah/ubah peran); false bila event sudah usang.</summary>
    public bool ApplyRole(string role, DateTimeOffset occurredAt)
    {
        if (occurredAt < LastEventAt) return false;

        Role = NormalizeRole(role);
        IsRemoved = false;
        LastEventAt = occurredAt;
        return true;
    }

    /// <summary>Menerapkan pengeluaran anggota; false bila event sudah usang.</summary>
    public bool Remove(DateTimeOffset occurredAt)
    {
        if (occurredAt < LastEventAt) return false;

        IsRemoved = true;
        LastEventAt = occurredAt;
        return true;
    }

    /// <summary>Anggota aktif yang bukan pengamat boleh membuat dan mengubah tugas.</summary>
    public bool CanWrite => !IsRemoved && Role != Viewer;

    public static string NormalizeRole(string role)
    {
        var upper = role.Trim().ToUpperInvariant();
        return upper is Manager or Member or Viewer
            ? upper
            : throw new ArgumentException($"Peran proyek tidak dikenal: {role}", nameof(role));
    }
}
