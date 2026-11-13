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
    public DateOnly? StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }

    /// <summary>ID publik user pemilik (milik User Service).</summary>
    public Guid OwnerId { get; private set; }

    /// <summary>ID publik departemen (milik User Service); null bila proyek tidak terikat departemen.</summary>
    public Guid? DepartmentId { get; private set; }

    public IReadOnlyCollection<ProjectMember> Members => _members;

    private Project() { } // untuk EF Core

    [GeneratedRegex("^[A-Z][A-Z0-9_-]*$")]
    private static partial Regex CodePattern();

    public static Project Create(
        long tenantId, string code, string name, string? description, DateOnly? startDate, DateOnly? endDate,
        Guid ownerId, Guid? departmentId)
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
        project.SetDetails(name, description, startDate, endDate, departmentId);
        project._members.Add(new ProjectMember(tenantId, ownerId, ProjectRole.Manager));
        project.AddDomainEvent(new ProjectCreatedDomainEvent(tenantId, project.PublicId, project.Code, project.Name, ownerId));
        return project;
    }

    /// <summary>Mengubah data proyek. Proyek yang diarsipkan tidak bisa diubah, dan arsip hanya lewat <see cref="Archive"/>.</summary>
    public void Update(string name, string? description, ProjectStatus status, DateOnly? startDate, DateOnly? endDate, Guid? departmentId)
    {
        if (Status == ProjectStatus.Archived) throw new DomainException("Proyek yang diarsipkan tidak bisa diubah.");
        if (status == ProjectStatus.Archived) throw new DomainException("Gunakan arsip proyek untuk mengarsipkan.");

        SetDetails(name, description, startDate, endDate, departmentId);
        Status = status;
        AddDomainEvent(new ProjectUpdatedDomainEvent(TenantId, PublicId, Code, Name, ToName(Status)));
    }

    /// <summary>Mengarsipkan proyek; mengulang arsip tidak berbuat apa-apa.</summary>
    public void Archive()
    {
        if (Status == ProjectStatus.Archived) return;

        Status = ProjectStatus.Archived;
        AddDomainEvent(new ProjectArchivedDomainEvent(TenantId, PublicId));
    }

    private void SetDetails(string name, string? description, DateOnly? startDate, DateOnly? endDate, Guid? departmentId)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Nama proyek wajib diisi.");
        if (name.Trim().Length > NameMaxLength) throw new DomainException($"Nama proyek maksimal {NameMaxLength} karakter.");

        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (trimmedDescription is { Length: > DescriptionMaxLength })
            throw new DomainException($"Deskripsi proyek maksimal {DescriptionMaxLength} karakter.");

        if (startDate is { } start && endDate is { } end && start > end)
            throw new DomainException("Tanggal mulai tidak boleh setelah tanggal selesai.");

        Name = name.Trim();
        Description = trimmedDescription;
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
}

public sealed record ProjectCreatedDomainEvent(long TenantId, Guid ProjectId, string Code, string Name, Guid OwnerId) : DomainEvent;

public sealed record ProjectUpdatedDomainEvent(long TenantId, Guid ProjectId, string Code, string Name, string Status) : DomainEvent;

public sealed record ProjectArchivedDomainEvent(long TenantId, Guid ProjectId) : DomainEvent;
