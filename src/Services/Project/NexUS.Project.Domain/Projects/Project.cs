using System.Text.RegularExpressions;
using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;

namespace NexUS.Project.Domain.Projects;

public enum ProjectStatus
{
    Planning,
    Active,
    OnHold,
    Completed,
    Archived,
}

public enum ProjectPriority
{
    Low,
    Medium,
    High,
    Urgent,
}

/// <summary>Proyek milik satu tenant. Pembuatnya menjadi pemilik sekaligus anggota berperan <see cref="ProjectRole.Manager"/>.</summary>
public sealed partial class Project : AggregateRoot
{
    public const int CodeMaxLength = 20;
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 2000;

    private readonly List<ProjectMember> _members = [];

    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public ProjectStatus Status { get; private set; }
    public ProjectPriority Priority { get; private set; }
    public DateOnly? StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }

    /// <summary>Persentase tugas selesai (0–100); dihitung dari tugas, belum diisi di story ini.</summary>
    public int ProgressPct { get; private set; }

    /// <summary>ID publik user pemilik (milik User Service).</summary>
    public Guid OwnerId { get; private set; }

    /// <summary>ID publik departemen (milik User Service); null bila proyek tidak terikat departemen.</summary>
    public Guid? DepartmentId { get; private set; }

    /// <summary>Anggota aktif; hanya terisi bila proyek dimuat beserta anggotanya.</summary>
    public IReadOnlyCollection<ProjectMember> Members => _members.Where(m => !m.IsDeleted).ToList();

    private Project() { } // untuk EF Core

    [GeneratedRegex("^[A-Z][A-Z0-9_-]*$")]
    private static partial Regex CodePattern();

    public static Project Create(
        long tenantId, string code, string name, string? description, ProjectPriority priority,
        DateOnly? startDate, DateOnly? endDate, Guid ownerId, Guid? departmentId, DateTimeOffset now)
    {
        if (tenantId <= 0) throw new DomainException("Tenant wajib diisi.");
        if (ownerId == Guid.Empty) throw new DomainException("Pemilik proyek wajib diisi.");

        var project = new Project
        {
            TenantId = tenantId,
            Code = NormalizeCode(code),
            Status = ProjectStatus.Planning,
            OwnerId = ownerId,
        };
        project.SetDetails(name, description, priority, startDate, endDate, departmentId);
        project._members.Add(new ProjectMember(tenantId, ownerId, ProjectRole.Manager, now));
        project.AddDomainEvent(new ProjectCreatedDomainEvent(tenantId, project.PublicId, project.Code, project.Name, ownerId));
        return project;
    }

    /// <summary>
    /// Mengubah data proyek. Proyek yang diarsipkan tidak bisa diubah, dan arsip hanya lewat <see cref="Archive"/>.
    /// Bila tidak ada nilai yang berubah, tidak ada event yang dicatat.
    /// </summary>
    public void Update(
        string name, string? description, ProjectStatus status, ProjectPriority priority,
        DateOnly? startDate, DateOnly? endDate, Guid? departmentId)
    {
        if (Status == ProjectStatus.Archived) throw new DomainException("Proyek yang diarsipkan tidak bisa diubah.");
        if (status == ProjectStatus.Archived) throw new DomainException("Gunakan arsip proyek untuk mengarsipkan.");

        var before = (Name, Description, Status, Priority, StartDate, EndDate, DepartmentId);
        SetDetails(name, description, priority, startDate, endDate, departmentId);
        Status = status;

        if (before != (Name, Description, Status, Priority, StartDate, EndDate, DepartmentId))
            AddDomainEvent(new ProjectUpdatedDomainEvent(TenantId, PublicId, Code, Name, ToName(Status)));
    }

    /// <summary>
    /// Rentang tanggal proyek harus tetap mencakup semua milestone-nya (<paramref name="earliestMilestone"/> s.d.
    /// <paramref name="latestMilestone"/>, null bila belum ada milestone).
    /// </summary>
    public void EnsureDatesCover(DateOnly? earliestMilestone, DateOnly? latestMilestone)
    {
        if ((StartDate is { } start && earliestMilestone is { } earliest && earliest < start)
            || (EndDate is { } end && latestMilestone is { } latest && latest > end))
            throw new DomainException(
                $"Rentang tanggal proyek harus mencakup semua milestone ({earliestMilestone:yyyy-MM-dd} s.d. {latestMilestone:yyyy-MM-dd}).");
    }

    /// <summary>Mengarsipkan proyek; mengulang arsip tidak berbuat apa-apa.</summary>
    public void Archive()
    {
        if (Status == ProjectStatus.Archived) return;

        Status = ProjectStatus.Archived;
        AddDomainEvent(new ProjectArchivedDomainEvent(TenantId, PublicId));
    }

    public ProjectMember AddMember(Guid userId, ProjectRole role, DateTimeOffset now)
    {
        EnsureMembersEditable();
        if (FindMember(userId) is not null) throw new DomainException("User sudah menjadi anggota proyek ini.");

        var member = new ProjectMember(TenantId, userId, role, now);
        _members.Add(member);
        MarkChildrenChanged();
        AddDomainEvent(new ProjectMemberAddedDomainEvent(TenantId, PublicId, userId, ProjectMember.ToName(role)));
        return member;
    }

    /// <summary>Mengganti peran anggota. Mengembalikan null bila user bukan anggota.</summary>
    public ProjectMember? ChangeMemberRole(Guid userId, ProjectRole role)
    {
        EnsureMembersEditable();
        if (FindMember(userId) is not { } member) return null;
        if (member.Role == role) return member;

        if (member.Role == ProjectRole.Manager) EnsureNotLastManager();
        member.ChangeRole(role);
        MarkChildrenChanged();
        AddDomainEvent(new ProjectMemberRoleChangedDomainEvent(TenantId, PublicId, userId, ProjectMember.ToName(role)));
        return member;
    }

    /// <summary>Mengeluarkan anggota. Mengembalikan false bila user bukan anggota.</summary>
    public bool RemoveMember(Guid userId)
    {
        EnsureMembersEditable();
        if (FindMember(userId) is not { } member) return false;

        if (member.Role == ProjectRole.Manager) EnsureNotLastManager();
        member.MarkDeleted();
        MarkChildrenChanged();
        AddDomainEvent(new ProjectMemberRemovedDomainEvent(TenantId, PublicId, userId));
        return true;
    }

    public ProjectMember? FindMember(Guid userId) => _members.FirstOrDefault(m => !m.IsDeleted && m.UserId == userId);

    private void EnsureMembersEditable()
    {
        if (Status == ProjectStatus.Archived) throw new DomainException("Anggota proyek yang diarsipkan tidak bisa diubah.");
    }

    private void EnsureNotLastManager()
    {
        if (_members.Count(m => !m.IsDeleted && m.Role == ProjectRole.Manager) <= 1)
            throw new DomainException("Proyek harus punya minimal satu manager; tunjuk manager lain lebih dulu.");
    }

    private void SetDetails(
        string name, string? description, ProjectPriority priority, DateOnly? startDate, DateOnly? endDate, Guid? departmentId)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Nama proyek wajib diisi.");
        if (name.Trim().Length > NameMaxLength) throw new DomainException($"Nama proyek maksimal {NameMaxLength} karakter.");

        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (trimmedDescription is { Length: > DescriptionMaxLength })
            throw new DomainException($"Deskripsi proyek maksimal {DescriptionMaxLength} karakter.");

        if (startDate is { } start && endDate is { } end && start > end)
            throw new DomainException("Tanggal mulai tidak boleh setelah tanggal selesai.");
        if (!Enum.IsDefined(priority)) throw new DomainException("Prioritas proyek tidak dikenal.");

        Name = name.Trim();
        Description = trimmedDescription;
        Priority = priority;
        StartDate = startDate;
        EndDate = endDate;
        DepartmentId = departmentId == Guid.Empty ? null : departmentId;
    }

    public static string NormalizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new DomainException("Kode proyek wajib diisi.");
        var normalized = code.Trim().ToUpperInvariant();
        if (normalized.Length > CodeMaxLength) throw new DomainException($"Kode proyek maksimal {CodeMaxLength} karakter.");
        if (!CodePattern().IsMatch(normalized))
            throw new DomainException("Kode proyek diawali huruf dan hanya berisi huruf, angka, garis bawah, atau tanda hubung.");
        return normalized;
    }

    /// <summary>Nama status di kolom STATUS dan di event (huruf besar, mis. <c>ON_HOLD</c>).</summary>
    public static string ToName(ProjectStatus status) => status switch
    {
        ProjectStatus.Planning => "PLANNING",
        ProjectStatus.Active => "ACTIVE",
        ProjectStatus.OnHold => "ON_HOLD",
        ProjectStatus.Completed => "COMPLETED",
        ProjectStatus.Archived => "ARCHIVED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseStatus(string? name, out ProjectStatus status)
    {
        foreach (var candidate in Enum.GetValues<ProjectStatus>())
        {
            if (!string.Equals(ToName(candidate), name?.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            status = candidate;
            return true;
        }

        status = default;
        return false;
    }

    /// <summary>Nama prioritas di kolom PRIORITY (huruf besar).</summary>
    public static string ToName(ProjectPriority priority) => priority.ToString().ToUpperInvariant();

    public static bool TryParsePriority(string? name, out ProjectPriority priority)
    {
        foreach (var candidate in Enum.GetValues<ProjectPriority>())
        {
            if (!string.Equals(ToName(candidate), name?.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            priority = candidate;
            return true;
        }

        priority = default;
        return false;
    }
}

public sealed record ProjectCreatedDomainEvent(long TenantId, Guid ProjectId, string Code, string Name, Guid OwnerId) : DomainEvent;

public sealed record ProjectUpdatedDomainEvent(long TenantId, Guid ProjectId, string Code, string Name, string Status) : DomainEvent;

public sealed record ProjectArchivedDomainEvent(long TenantId, Guid ProjectId) : DomainEvent;

public sealed record ProjectMemberAddedDomainEvent(long TenantId, Guid ProjectId, Guid UserId, string Role) : DomainEvent;

public sealed record ProjectMemberRoleChangedDomainEvent(long TenantId, Guid ProjectId, Guid UserId, string Role) : DomainEvent;

public sealed record ProjectMemberRemovedDomainEvent(long TenantId, Guid ProjectId, Guid UserId) : DomainEvent;
