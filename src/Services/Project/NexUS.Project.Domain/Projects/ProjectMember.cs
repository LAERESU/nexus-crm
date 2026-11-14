using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;

namespace NexUS.Project.Domain.Projects;

public enum ProjectRole
{
    Manager,
    Member,
    Viewer,
}

/// <summary>Keanggotaan seorang user di satu proyek; diubah lewat aggregate <see cref="Project"/>.</summary>
public sealed class ProjectMember : Entity
{
    public long ProjectId { get; private set; }

    /// <summary>ID publik user (milik User Service).</summary>
    public Guid UserId { get; private set; }

    public ProjectRole Role { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }

    private ProjectMember() { } // untuk EF Core

    internal ProjectMember(long tenantId, Guid userId, ProjectRole role, DateTimeOffset joinedAt)
    {
        if (userId == Guid.Empty) throw new DomainException("User anggota wajib diisi.");

        TenantId = tenantId;
        UserId = userId;
        Role = role;
        JoinedAt = joinedAt;
    }

    internal void ChangeRole(ProjectRole role) => Role = role;

    /// <summary>Hanya nama peran utuh (tanpa angka atau gabungan seperti <c>manager,member</c>).</summary>
    public static bool TryParseRole(string? name, out ProjectRole role)
    {
        foreach (var candidate in Enum.GetValues<ProjectRole>())
        {
            if (!string.Equals(ToName(candidate), name?.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            role = candidate;
            return true;
        }

        role = default;
        return false;
    }

    /// <summary>Nama peran di kolom PROJECT_ROLE (huruf besar).</summary>
    public static string ToName(ProjectRole role) => role.ToString().ToUpperInvariant();
}
