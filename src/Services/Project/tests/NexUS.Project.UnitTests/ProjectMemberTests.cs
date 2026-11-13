using FluentAssertions;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.Project.Application.Projects;
using NexUS.Project.Domain.Projects;
using NexUS.Project.Domain.Users;
using ProjectEntity = NexUS.Project.Domain.Projects.Project;

namespace NexUS.Project.UnitTests;

public class ProjectMemberTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Owner = Guid.NewGuid();

    private static ProjectEntity NewProject()
    {
        var project = ProjectEntity.Create(7, "WEB", "Portal", null, ProjectPriority.Medium, null, null, Owner, null, Now);
        project.PopDomainEvents();
        return project;
    }

    [Fact]
    public void AddMember_menambah_anggota_dan_menolak_duplikat()
    {
        var project = NewProject();
        var user = Guid.NewGuid();

        var member = project.AddMember(user, ProjectRole.Viewer, Now.AddHours(1));

        member.Should().Match<ProjectMember>(m => m.UserId == user && m.Role == ProjectRole.Viewer && m.TenantId == 7 && m.JoinedAt == Now.AddHours(1));
        project.Members.Should().HaveCount(2);
        project.PopDomainEvents().Should().ContainSingle().Which.Should().BeOfType<ProjectMemberAddedDomainEvent>().Which.Role.Should().Be("VIEWER");
        FluentActions.Invoking(() => project.AddMember(user, ProjectRole.Member, Now)).Should().Throw<DomainException>();
        FluentActions.Invoking(() => project.AddMember(Guid.Empty, ProjectRole.Member, Now)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Manager_terakhir_tidak_bisa_dikeluarkan_atau_diturunkan()
    {
        var project = NewProject();

        FluentActions.Invoking(() => project.RemoveMember(Owner)).Should().Throw<DomainException>();
        FluentActions.Invoking(() => project.ChangeMemberRole(Owner, ProjectRole.Member)).Should().Throw<DomainException>();
        project.FindMember(Owner)!.Role.Should().Be(ProjectRole.Manager);

        var second = Guid.NewGuid();
        project.AddMember(second, ProjectRole.Manager, Now);
        project.PopDomainEvents();

        project.RemoveMember(Owner).Should().BeTrue();
        project.Members.Should().ContainSingle().Which.UserId.Should().Be(second);
        project.PopDomainEvents().Should().ContainSingle().Which.Should().BeOfType<ProjectMemberRemovedDomainEvent>();
        FluentActions.Invoking(() => project.ChangeMemberRole(second, ProjectRole.Viewer)).Should().Throw<DomainException>();
    }

    [Fact]
    public void ChangeMemberRole_mencatat_event_hanya_bila_peran_berubah()
    {
        var project = NewProject();
        var user = Guid.NewGuid();
        project.AddMember(user, ProjectRole.Member, Now);
        project.PopDomainEvents();

        project.ChangeMemberRole(user, ProjectRole.Member)!.Role.Should().Be(ProjectRole.Member);
        project.PopDomainEvents().Should().BeEmpty();

        project.ChangeMemberRole(user, ProjectRole.Manager)!.Role.Should().Be(ProjectRole.Manager);
        project.PopDomainEvents().Should().ContainSingle().Which.Should().BeOfType<ProjectMemberRoleChangedDomainEvent>();

        project.ChangeMemberRole(Guid.NewGuid(), ProjectRole.Member).Should().BeNull("bukan anggota");
        project.RemoveMember(Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void Anggota_yang_dikeluarkan_bisa_ditambahkan_lagi_dan_proyek_arsip_tidak_bisa_diubah_anggotanya()
    {
        var project = NewProject();
        var user = Guid.NewGuid();
        project.AddMember(user, ProjectRole.Member, Now);
        project.RemoveMember(user);

        project.FindMember(user).Should().BeNull();
        project.AddMember(user, ProjectRole.Viewer, Now).Role.Should().Be(ProjectRole.Viewer);

        project.Archive();
        FluentActions.Invoking(() => project.AddMember(Guid.NewGuid(), ProjectRole.Member, Now)).Should().Throw<DomainException>();
        FluentActions.Invoking(() => project.RemoveMember(user)).Should().Throw<DomainException>();
        FluentActions.Invoking(() => project.ChangeMemberRole(user, ProjectRole.Member)).Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData("manager", true)]
    [InlineData(" VIEWER ", true)]
    [InlineData("owner", false)]
    [InlineData("1", false)]
    [InlineData(null, false)]
    public void Nama_peran_dikenali_tanpa_membedakan_huruf(string? name, bool expected) =>
        ProjectRoleNames.TryParse(name, out _).Should().Be(expected);

    [Fact]
    public void UserRef_mengabaikan_event_yang_lebih_lama()
    {
        var user = UserRef.Create(7, Guid.NewGuid());

        user.ApplyProfile("a@b.c", "Nama Baru", true, Now.AddMinutes(5)).Should().BeTrue();
        user.ApplyProfile("lama@b.c", "Nama Lama", true, Now).Should().BeFalse();
        user.Deactivate(Now.AddMinutes(1)).Should().BeFalse();
        user.Should().Match<UserRef>(u => u.FullName == "Nama Baru" && u.Email == "a@b.c" && u.IsActive);

        user.Deactivate(Now.AddMinutes(6)).Should().BeTrue();
        user.IsActive.Should().BeFalse();
        user.ApplyProfile("a@b.c", "Nama Baru", true, Now.AddMinutes(7)).Should().BeTrue("diaktifkan kembali");
        user.IsActive.Should().BeTrue();
    }
}
