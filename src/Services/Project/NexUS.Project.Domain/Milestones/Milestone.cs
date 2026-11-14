using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;
using ProjectEntity = NexUS.Project.Domain.Projects.Project;
using ProjectStatus = NexUS.Project.Domain.Projects.ProjectStatus;

namespace NexUS.Project.Domain.Milestones;

public enum MilestoneStatus
{
    Open,
    Completed,
}

/// <summary>
/// Milestone satu proyek. Tanggal jatuh temponya harus di dalam rentang tanggal proyek (bila rentang itu diisi), dan
/// milestone proyek yang diarsipkan tidak bisa diubah.
/// </summary>
public sealed class Milestone : AggregateRoot
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 2000;

    /// <summary>ID internal proyek pemilik.</summary>
    public long ProjectId { get; private set; }

    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public DateOnly DueDate { get; private set; }
    public MilestoneStatus Status { get; private set; }

    private Milestone() { } // untuk EF Core

    public static Milestone Create(ProjectEntity project, string name, string? description, DateOnly dueDate)
    {
        var milestone = new Milestone { TenantId = project.TenantId, ProjectId = project.Id, Status = MilestoneStatus.Open };
        milestone.Apply(project, name, description, dueDate);
        project.RegisterMilestoneChange();
        milestone.AddDomainEvent(new MilestoneCreatedDomainEvent(
            project.TenantId, project.PublicId, milestone.PublicId, milestone.Name, milestone.DueDate));
        return milestone;
    }

    /// <summary>Mengubah milestone; tanpa perubahan nilai tidak ada event.</summary>
    public void Update(ProjectEntity project, string name, string? description, DateOnly dueDate, MilestoneStatus status)
    {
        if (project.Id != ProjectId) throw new InvalidOperationException("Milestone bukan milik proyek ini.");

        var before = (Name, Description, DueDate, Status);
        Apply(project, name, description, dueDate);
        if (DueDate != before.DueDate) project.RegisterMilestoneChange();
        Status = status;

        if (before != (Name, Description, DueDate, Status))
            AddDomainEvent(new MilestoneUpdatedDomainEvent(TenantId, project.PublicId, PublicId, Name, DueDate, ToName(Status)));
    }

    private void Apply(ProjectEntity project, string name, string? description, DateOnly dueDate)
    {
        if (project.Status == ProjectStatus.Archived) throw new DomainException("Milestone proyek yang diarsipkan tidak bisa diubah.");
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Nama milestone wajib diisi.");
        if (name.Trim().Length > NameMaxLength) throw new DomainException($"Nama milestone maksimal {NameMaxLength} karakter.");

        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (trimmedDescription is { Length: > DescriptionMaxLength })
            throw new DomainException($"Deskripsi milestone maksimal {DescriptionMaxLength} karakter.");

        EnsureWithinProject(project, dueDate);

        Name = name.Trim();
        Description = trimmedDescription;
        DueDate = dueDate;
    }

    /// <summary>Tanggal jatuh tempo harus di dalam rentang tanggal proyek; batas yang kosong tidak membatasi.</summary>
    public static void EnsureWithinProject(ProjectEntity project, DateOnly dueDate)
    {
        if ((project.StartDate is { } start && dueDate < start) || (project.EndDate is { } end && dueDate > end))
            throw new DomainException(
                $"Tanggal milestone harus di dalam rentang tanggal proyek ({ProjectEntity.Format(project.StartDate)} s.d. {ProjectEntity.Format(project.EndDate)}).");
    }

    /// <summary>Nama status di kolom STATUS (huruf besar).</summary>
    public static string ToName(MilestoneStatus status) => status.ToString().ToUpperInvariant();

    /// <summary>Hanya nama status utuh (<c>open</c>, <c>completed</c>).</summary>
    public static bool TryParseStatus(string? name, out MilestoneStatus status)
    {
        foreach (var candidate in Enum.GetValues<MilestoneStatus>())
        {
            if (!string.Equals(ToName(candidate), name?.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            status = candidate;
            return true;
        }

        status = default;
        return false;
    }
}

public sealed record MilestoneCreatedDomainEvent(long TenantId, Guid ProjectId, Guid MilestoneId, string Name, DateOnly DueDate) : DomainEvent;

public sealed record MilestoneUpdatedDomainEvent(
    long TenantId, Guid ProjectId, Guid MilestoneId, string Name, DateOnly DueDate, string Status) : DomainEvent;
