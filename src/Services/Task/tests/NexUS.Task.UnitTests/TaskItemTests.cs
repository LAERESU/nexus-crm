using FluentAssertions;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.Tasks.Application.Tasks;
using NexUS.Tasks.Domain.Projects;
using NexUS.Tasks.Domain.Tasks;

namespace NexUS.Tasks.UnitTests;

public class TaskItemTests
{
    private static readonly Guid Project = Guid.NewGuid();
    private static readonly Guid Reporter = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 8, 0, 0, TimeSpan.Zero);

    private static TaskItem NewTask(TaskItem? parent = null, Guid? project = null) =>
        TaskItem.Create(7, project ?? Project, parent, " Desain halaman ", "  ", TaskPriority.High, new DateOnly(2026, 12, 1), Reporter);

    /// <summary>Tugas tersimpan (punya ID internal), agar bisa menjadi induk.</summary>
    private static TaskItem Saved(TaskItem task, long id)
    {
        typeof(NexUS.BuildingBlocks.Common.Domain.Entity).GetProperty("Id")!.SetValue(task, id);
        return task;
    }

    [Fact]
    public void Create_menormalkan_data_berstatus_todo_dan_mencatat_event()
    {
        var task = NewTask();

        task.Should().Match<TaskItem>(t =>
            t.Title == "Desain halaman" && t.Description == null && t.Status == TaskItemStatus.Todo && t.Priority == TaskPriority.High
            && t.ProjectId == Project && t.ReporterId == Reporter && t.ParentTaskId == null && t.TenantId == 7);
        task.PopDomainEvents().Should().ContainSingle().Which.Should().BeOfType<TaskCreatedDomainEvent>();
    }

    [Fact]
    public void Subtask_satu_tingkat_diterima_subtask_dari_subtask_ditolak()
    {
        var parent = Saved(NewTask(), 10);
        var child = Saved(NewTask(parent), 11);

        child.ParentTaskId.Should().Be(10);
        FluentActions.Invoking(() => NewTask(child)).Should().Throw<DomainException>().WithMessage("*maksimum satu tingkat*");
        FluentActions.Invoking(() => NewTask(parent, Guid.NewGuid())).Should().Throw<DomainException>().WithMessage("*proyek yang sama*");

        parent.Delete();
        FluentActions.Invoking(() => NewTask(parent)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Validasi_judul_dan_data_wajib()
    {
        FluentActions.Invoking(() => TaskItem.Create(7, Project, null, " ", null, TaskPriority.Low, null, Reporter)).Should().Throw<DomainException>();
        FluentActions.Invoking(() => TaskItem.Create(7, Project, null, new string('x', 301), null, TaskPriority.Low, null, Reporter)).Should().Throw<DomainException>();
        FluentActions.Invoking(() => TaskItem.Create(0, Project, null, "X", null, TaskPriority.Low, null, Reporter)).Should().Throw<DomainException>();
        FluentActions.Invoking(() => TaskItem.Create(7, Guid.Empty, null, "X", null, TaskPriority.Low, null, Reporter)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Update_mencatat_event_hanya_bila_berubah_dan_tugas_terhapus_tidak_bisa_diubah()
    {
        var task = NewTask();
        task.PopDomainEvents();

        task.Update("Desain halaman", null, TaskPriority.High, new DateOnly(2026, 12, 1));
        task.PopDomainEvents().Should().BeEmpty();

        task.Update("Baru", "Isi", TaskPriority.Low, null);
        task.Should().Match<TaskItem>(t => t.Title == "Baru" && t.Description == "Isi" && t.DueDate == null);
        task.PopDomainEvents().Should().ContainSingle().Which.Should().BeOfType<TaskUpdatedDomainEvent>();

        task.Delete();
        task.Delete();
        task.IsDeleted.Should().BeTrue();
        task.PopDomainEvents().Should().ContainSingle().Which.Should().BeOfType<TaskDeletedDomainEvent>();
        FluentActions.Invoking(() => task.Update("X", null, TaskPriority.Low, null)).Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData("in_progress", TaskItemStatus.InProgress)]
    [InlineData(" DONE ", TaskItemStatus.Done)]
    public void Nama_status_dikenali(string name, TaskItemStatus expected)
    {
        TaskNames.TryParseStatus(name, out var status).Should().BeTrue();
        status.Should().Be(expected);
        TaskNames.TryParseStatus("selesai", out _).Should().BeFalse();
        TaskNames.TryParseStatus("in_progress,done", out _).Should().BeFalse();
    }

    [Fact]
    public void Read_model_anggota_mengabaikan_event_lama_dan_pengamat_tidak_bisa_menulis()
    {
        var member = ProjectMemberRef.Create(7, Project, Reporter);
        member.ApplyRole("member", Now).Should().BeTrue();
        member.CanWrite.Should().BeTrue();

        member.Remove(Now.AddMinutes(1)).Should().BeTrue();
        member.ApplyRole("manager", Now).Should().BeFalse("event lebih lama dari pengeluaran");
        member.CanWrite.Should().BeFalse();

        member.ApplyRole("viewer", Now.AddMinutes(2)).Should().BeTrue();
        member.Role.Should().Be(ProjectMemberRef.Viewer);
        member.CanWrite.Should().BeFalse();
        FluentActions.Invoking(() => member.ApplyRole("pemilik", Now.AddMinutes(3))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Read_model_proyek_mengabaikan_detail_lama_dan_arsip_final()
    {
        var project = ProjectRef.Create(7, Project);
        project.ApplyDetails("WEB", "Portal", Now.AddMinutes(1)).Should().BeTrue();
        project.ApplyDetails("OLD", "Lama", Now).Should().BeFalse();
        project.Should().Match<ProjectRef>(p => p.Code == "WEB" && p.Name == "Portal");

        project.Archive();
        project.ApplyDetails("WEB", "Portal Baru", Now.AddMinutes(2));
        project.IsArchived.Should().BeTrue();
    }
}
