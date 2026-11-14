using FluentAssertions;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.Project.Application.Milestones;
using NexUS.Project.Domain.Milestones;
using NexUS.Project.Domain.Projects;
using ProjectEntity = NexUS.Project.Domain.Projects.Project;

namespace NexUS.Project.UnitTests;

public class MilestoneTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Start = new(2026, 12, 1);
    private static readonly DateOnly End = new(2026, 12, 31);

    private static ProjectEntity NewProject(DateOnly? start = null, DateOnly? end = null) =>
        ProjectEntity.Create(7, "WEB", "Portal", null, ProjectPriority.Medium, start, end, Guid.NewGuid(), null, Now);

    [Fact]
    public void Create_di_dalam_rentang_proyek_termasuk_batasnya()
    {
        var project = NewProject(Start, End);

        var milestone = Milestone.Create(project, " Rilis Beta ", "  ", Start);

        milestone.Should().Match<Milestone>(m =>
            m.Name == "Rilis Beta" && m.Description == null && m.DueDate == Start && m.Status == MilestoneStatus.Open && m.TenantId == 7);
        milestone.PopDomainEvents().Should().ContainSingle().Which.Should().BeOfType<MilestoneCreatedDomainEvent>()
            .Which.ProjectId.Should().Be(project.PublicId);
        Milestone.Create(project, "Akhir", null, End).DueDate.Should().Be(End);
    }

    [Theory]
    [InlineData(2026, 11, 30)]
    [InlineData(2027, 1, 1)]
    public void Tanggal_di_luar_rentang_proyek_ditolak(int year, int month, int day)
    {
        var project = NewProject(Start, End);

        FluentActions.Invoking(() => Milestone.Create(project, "M", null, new DateOnly(year, month, day)))
            .Should().Throw<DomainException>().WithMessage("*2026-12-01 s.d. 2026-12-31*");
    }

    [Fact]
    public void Batas_rentang_yang_kosong_tidak_membatasi()
    {
        Milestone.Create(NewProject(), "M", null, new DateOnly(2030, 1, 1)).Should().NotBeNull();
        Milestone.Create(NewProject(start: Start), "M", null, new DateOnly(2030, 1, 1)).Should().NotBeNull();
        FluentActions.Invoking(() => Milestone.Create(NewProject(start: Start), "M", null, new DateOnly(2026, 1, 1)))
            .Should().Throw<DomainException>().WithMessage("*tanpa batas*");
    }

    [Fact]
    public void Update_memeriksa_rentang_dan_mencatat_event_hanya_bila_berubah()
    {
        var project = NewProject(Start, End);
        var milestone = Milestone.Create(project, "M", null, Start);
        milestone.PopDomainEvents();

        milestone.Update(project, "M", null, Start, MilestoneStatus.Open);
        milestone.PopDomainEvents().Should().BeEmpty();

        milestone.Update(project, "M2", "Catatan", End, MilestoneStatus.Completed);
        milestone.Should().Match<Milestone>(m => m.Name == "M2" && m.DueDate == End && m.Status == MilestoneStatus.Completed);
        milestone.PopDomainEvents().Should().ContainSingle().Which.Should().BeOfType<MilestoneUpdatedDomainEvent>()
            .Which.Status.Should().Be("COMPLETED");

        FluentActions.Invoking(() => milestone.Update(project, "M2", null, End.AddDays(1), MilestoneStatus.Open))
            .Should().Throw<DomainException>();
        milestone.DueDate.Should().Be(End, "perubahan yang ditolak tidak mengubah apa pun");
    }

    [Fact]
    public void Milestone_proyek_arsip_tidak_bisa_dibuat_atau_diubah()
    {
        var project = NewProject(Start, End);
        var milestone = Milestone.Create(project, "M", null, Start);
        project.Archive();

        FluentActions.Invoking(() => Milestone.Create(project, "Baru", null, Start)).Should().Throw<DomainException>();
        FluentActions.Invoking(() => milestone.Update(project, "M", null, Start, MilestoneStatus.Completed)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Rentang_proyek_harus_mencakup_milestone_yang_ada()
    {
        var project = NewProject(Start, End);

        project.EnsureDatesCover(null, null);
        project.EnsureDatesCover(Start, End);
        FluentActions.Invoking(() => project.EnsureDatesCover(Start.AddDays(-1), End)).Should().Throw<DomainException>();
        FluentActions.Invoking(() => project.EnsureDatesCover(Start, End.AddDays(1))).Should().Throw<DomainException>();
        NewProject().EnsureDatesCover(new DateOnly(2020, 1, 1), new DateOnly(2040, 1, 1));
    }

    [Theory]
    [InlineData("open", true)]
    [InlineData(" COMPLETED ", true)]
    [InlineData("done", false)]
    [InlineData("open,completed", false)]
    [InlineData(null, false)]
    public void Nama_status_milestone(string? name, bool expected) =>
        MilestoneStatusNames.TryParse(name, out _).Should().Be(expected);

    [Fact]
    public async Task Validator_menolak_nama_kosong_tanggal_kosong_dan_status_tidak_dikenal()
    {
        var create = new CreateMilestoneCommandValidator();
        (await create.ValidateAsync(new CreateMilestoneCommand(Guid.NewGuid(), "", null, Start))).IsValid.Should().BeFalse();
        (await create.ValidateAsync(new CreateMilestoneCommand(Guid.NewGuid(), "M", null, default))).IsValid.Should().BeFalse();
        (await create.ValidateAsync(new CreateMilestoneCommand(Guid.NewGuid(), "M", null, Start))).IsValid.Should().BeTrue();

        var update = new UpdateMilestoneCommandValidator();
        (await update.ValidateAsync(new UpdateMilestoneCommand(Guid.NewGuid(), Guid.NewGuid(), "M", null, Start, "done", 0))).IsValid.Should().BeFalse();
        (await update.ValidateAsync(new UpdateMilestoneCommand(Guid.NewGuid(), Guid.NewGuid(), "M", null, Start, "completed", 0))).IsValid.Should().BeTrue();
    }
}
