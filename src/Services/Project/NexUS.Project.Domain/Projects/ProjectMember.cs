using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;

namespace NexUS.Project.Domain.Projects;

public enum ProjectRole
{
    Manager,
    Member,
    Viewer,
}

/// <summary>Keanggotaan seorang user di satu proyek. Endpoint kelola anggota menyusul di NX-026.</summary>
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

    /// <summary>Nama peran di kolom PROJECT_ROLE (huruf besar).</summary>
    public static string ToName(ProjectRole role) => role.ToString().ToUpperInvariant();
}
