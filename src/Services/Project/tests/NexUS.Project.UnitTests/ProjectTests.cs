using FluentAssertions;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.Project.Application.Projects;
using NexUS.Project.Domain.Projects;
using ProjectEntity = NexUS.Project.Domain.Projects.Project;

namespace NexUS.Project.UnitTests;

public class ProjectTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly DateOnly Start = new(2026, 11, 16);
    private static readonly DateOnly End = new(2026, 12, 31);

    private static ProjectEntity NewProject(DateOnly? start = null, DateOnly? end = null) =>
        ProjectEntity.Create(7, " web-01 ", " Portal Karyawan ", "  ", start, end, Owner, null);

    [Fact]
    public void Create_menormalkan_data_dan_menjadikan_pembuat_anggota_manager()
    {
        var project = NewProject(Start, End);

        project.TenantId.Should().Be(7);
        project.Code.Should().Be("WEB-01");
        project.Name.Should().Be("Portal Karyawan");
        project.Description.Should().BeNull();
        project.Status.Should().Be(ProjectStatus.Planning);
        project.OwnerId.Should().Be(Owner);
        project.Members.Should().ContainSingle()
            .Which.Should().Match<ProjectMember>(m => m.UserId == Owner && m.Role == ProjectRole.Manager && m.TenantId == 7);
        project.PopDomainEvents().Should().ContainSingle()
            .Which.Should().BeOfType<ProjectCreatedDomainEvent>()
            .Which.Should().Match<ProjectCreatedDomainEvent>(e => e.ProjectId == project.PublicId && e.Code == "WEB-01" && e.OwnerId == Owner);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1ABC")]
    [InlineData("AB C")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTU")] // 21 karakter
    public void Kode_tidak_valid_ditolak(string code)
    {
        FluentActions.Invoking(() => ProjectEntity.Create(7, code, "Nama", null, null, null, Owner, null))
            .Should().Throw<DomainException>();
    }

    [Fact]
    public void Tenant_pemilik_dan_nama_wajib_diisi()
    {
        FluentActions.Invoking(() => ProjectEntity.Create(0, "A", "Nama", null, null, null, Owner, null)).Should().Throw<DomainException>();
        FluentActions.Invoking(() => ProjectEntity.Create(7, "A", "Nama", null, null, null, Guid.Empty, null)).Should().Throw<DomainException>();
        FluentActions.Invoking(() => ProjectEntity.Create(7, "A", "  ", null, null, null, Owner, null)).Should().Throw<DomainException>();
        FluentActions.Invoking(() => ProjectEntity.Create(7, "A", new string('n', 201), null, null, null, Owner, null)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Tanggal_mulai_setelah_tanggal_selesai_ditolak_tanggal_sama_diterima()
    {
        FluentActions.Invoking(() => NewProject(End, Start)).Should().Throw<DomainException>();
        NewProject(Start, Start).EndDate.Should().Be(Start);
        NewProject(null, End).StartDate.Should().BeNull();

        var project = NewProject(Start, End);
        FluentActions.Invoking(() => project.Update("Nama", null, ProjectStatus.Active, End, Start, null)).Should().Throw<DomainException>();
        project.Status.Should().Be(ProjectStatus.Planning, "perubahan yang ditolak tidak mengubah apa pun");
    }

    [Fact]
    public void Update_mengganti_data_dan_status_serta_mencatat_event()
    {
        var project = NewProject();
        project.PopDomainEvents();
        var department = Guid.NewGuid();

        project.Update("Portal Baru", "Deskripsi", ProjectStatus.OnHold, Start, End, department);

        project.Name.Should().Be("Portal Baru");
        project.Description.Should().Be("Deskripsi");
        project.Status.Should().Be(ProjectStatus.OnHold);
        project.DepartmentId.Should().Be(department);
        project.Code.Should().Be("WEB-01", "kode tidak bisa diubah");
        project.PopDomainEvents().Should().ContainSingle()
            .Which.Should().BeOfType<ProjectUpdatedDomainEvent>().Which.Status.Should().Be("ON_HOLD");
    }

    [Fact]
    public void Arsip_hanya_lewat_Archive_dan_proyek_arsip_tidak_bisa_diubah()
    {
        var project = NewProject();
        project.PopDomainEvents();

        FluentActions.Invoking(() => project.Update("Nama", null, ProjectStatus.Archived, null, null, null)).Should().Throw<DomainException>();

        project.Archive();
        project.Archive(); // mengulang tidak berbuat apa-apa

        project.Status.Should().Be(ProjectStatus.Archived);
        project.IsDeleted.Should().BeFalse("arsip adalah status, bukan penghapusan baris");
        project.PopDomainEvents().Should().ContainSingle().Which.Should().BeOfType<ProjectArchivedDomainEvent>();
        FluentActions.Invoking(() => project.Update("Nama", null, ProjectStatus.Active, null, null, null)).Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData("planning", ProjectStatus.Planning)]
    [InlineData("ON_HOLD", ProjectStatus.OnHold)]
    [InlineData(" archived ", ProjectStatus.Archived)]
    public void Nama_status_dikenali_tanpa_membedakan_huruf(string name, ProjectStatus expected)
    {
        ProjectStatusNames.TryParse(name, out var status).Should().BeTrue();
        status.Should().Be(expected);
        ProjectStatusNames.ToApi(expected).Should().Be(name.Trim().ToLowerInvariant());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("onhold")]
    public void Nama_status_tidak_dikenal_ditolak(string? name) =>
        ProjectStatusNames.TryParse(name, out _).Should().BeFalse();

    [Fact]
    public async Task Validator_menolak_status_arsip_tanggal_terbalik_dan_kode_buruk()
    {
        var update = new UpdateProjectCommandValidator();
        (await update.ValidateAsync(new UpdateProjectCommand(Guid.NewGuid(), "Nama", null, "archived", null, null, null, 0))).IsValid.Should().BeFalse();
        (await update.ValidateAsync(new UpdateProjectCommand(Guid.NewGuid(), "Nama", null, "active", End, Start, null, 0))).IsValid.Should().BeFalse();
        (await update.ValidateAsync(new UpdateProjectCommand(Guid.NewGuid(), "Nama", null, "active", Start, End, null, 0))).IsValid.Should().BeTrue();

        var create = new CreateProjectCommandValidator();
        (await create.ValidateAsync(new CreateProjectCommand("1x", "Nama", null, null, null, null))).IsValid.Should().BeFalse();
        (await create.ValidateAsync(new CreateProjectCommand("web-01", "Nama", null, Start, End, null))).IsValid.Should().BeTrue();

        var list = new ListProjectsQueryValidator();
        (await list.ValidateAsync(new ListProjectsQuery(null, "tidak-ada", null, null))).IsValid.Should().BeFalse();
        (await list.ValidateAsync(new ListProjectsQuery(null, null, null, null, 1, 101))).IsValid.Should().BeFalse();
    }
}
