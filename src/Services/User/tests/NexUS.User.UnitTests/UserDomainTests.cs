using FluentAssertions;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.User.Domain.Users;
using UserEntity = NexUS.User.Domain.Users.User;

namespace NexUS.User.UnitTests;

public class UserDomainTests
{
    private static UserEntity NewUser() => UserEntity.Create(1, "Budi@Contoh.COM ", "Budi Santoso", "Staf", departmentId: null);

    [Fact]
    public void Create_menormalkan_email_ke_huruf_kecil_dan_aktif()
    {
        var user = NewUser();

        user.Email.Should().Be("budi@contoh.com");
        user.Status.Should().Be(UserStatus.Active);
    }

    [Fact]
    public void Create_mencatat_event_UserCreated()
    {
        var user = NewUser();

        user.PopDomainEvents().Should().ContainSingle()
            .Which.Should().BeOfType<UserCreatedDomainEvent>()
            .Which.Should().Match<UserCreatedDomainEvent>(e => e.TenantId == 1 && e.Email == "budi@contoh.com" && e.UserId == user.PublicId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_menolak_email_atau_nama_kosong(string value)
    {
        FluentActions.Invoking(() => UserEntity.Create(1, value, "Budi", null, null)).Should().Throw<DomainException>();
        FluentActions.Invoking(() => UserEntity.Create(1, "budi@contoh.com", value, null, null)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_menolak_tenant_tidak_valid()
    {
        FluentActions.Invoking(() => UserEntity.Create(0, "budi@contoh.com", "Budi", null, null)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Deactivate_mencatat_event_sekali_saja()
    {
        var user = NewUser();
        user.PopDomainEvents();

        user.Deactivate();
        user.Deactivate(); // sudah nonaktif: tidak ada event kedua

        user.Status.Should().Be(UserStatus.Inactive);
        var events = user.PopDomainEvents();
        events.Should().HaveCount(2); // UserDeactivated + RoleChanged (cache permission user itu dihapus)
        events.OfType<UserDeactivatedDomainEvent>().Should().ContainSingle();
        events.OfType<NexUS.User.Domain.Roles.RoleChangedDomainEvent>().Should().ContainSingle()
            .Which.Reason.Should().Be(NexUS.User.Domain.Roles.RoleChangeReason.UserStatusChanged);
    }

    [Fact]
    public void Activate_pada_user_aktif_tidak_mencatat_event()
    {
        var user = NewUser();
        user.PopDomainEvents();

        user.Activate();

        user.PopDomainEvents().Should().BeEmpty();
    }

    [Fact]
    public void UpdateProfile_mengubah_data_dan_mencatat_event_UserUpdated()
    {
        var user = NewUser();
        user.PopDomainEvents();

        user.UpdateProfile("Budi S.", "  ", departmentId: 7);

        user.FullName.Should().Be("Budi S.");
        user.JobTitle.Should().BeNull();
        user.DepartmentId.Should().Be(7);
        user.PopDomainEvents().Should().ContainSingle().Which.Should().BeOfType<UserUpdatedDomainEvent>();
    }

    [Fact]
    public void UpdateProfile_tanpa_perubahan_tidak_mencatat_event()
    {
        var user = NewUser();
        user.PopDomainEvents();

        user.UpdateProfile("Budi Santoso", "Staf", departmentId: null);

        user.PopDomainEvents().Should().BeEmpty();
    }
}
