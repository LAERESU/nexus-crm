using FluentAssertions;
using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.User.Application.Abstractions;
using NexUS.User.Application.Common;
using NexUS.User.Application.Roles;
using NexUS.User.Application.Users;
using NexUS.User.Domain.Departments;
using NexUS.User.Domain.Roles;
using NSubstitute;
using UserEntity = NexUS.User.Domain.Users.User;

namespace NexUS.User.UnitTests;

public class RbacHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRoleRepository _roles = Substitute.For<IRoleRepository>();
    private readonly IPermissionRepository _permissions = Substitute.For<IPermissionRepository>();
    private readonly IDepartmentRepository _departments = Substitute.For<IDepartmentRepository>();
    private readonly IUserPermissionReadService _permissionService = Substitute.For<IUserPermissionReadService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

    private SetUserRolesCommandHandler SetRolesHandler() => new(_users, _roles, _departments, _permissionService, _unitOfWork);

    [Fact]
    public async Task GetPermissions_user_biasa_tidak_boleh_membaca_milik_orang_lain()
    {
        _currentUser.UserId.Returns(Guid.NewGuid());
        _currentUser.Roles.Returns(["member"]);
        var handler = new GetUserPermissionsQueryHandler(_permissionService, _currentUser);

        await FluentActions.Awaiting(() => handler.Handle(new GetUserPermissionsQuery(Guid.NewGuid()), default))
            .Should().ThrowAsync<ForbiddenException>();

        await _permissionService.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPermissions_user_boleh_membaca_miliknya_sendiri_dan_admin_boleh_semua()
    {
        var self = Guid.NewGuid();
        _permissionService.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ci => new UserPermissionsDto(ci.Arg<Guid>(), [], []));

        _currentUser.UserId.Returns(self);
        _currentUser.Roles.Returns(["member"]);
        var asSelf = new GetUserPermissionsQueryHandler(_permissionService, _currentUser);
        (await asSelf.Handle(new GetUserPermissionsQuery(self), default)).UserId.Should().Be(self);

        _currentUser.Roles.Returns(["admin"]);
        var other = Guid.NewGuid();
        (await asSelf.Handle(new GetUserPermissionsQuery(other), default)).UserId.Should().Be(other);
    }

    [Fact]
    public async Task SetUserRoles_role_tidak_dikenal_menghasilkan_NotFound_dan_tidak_menyimpan()
    {
        var user = UserEntity.Create(1, "budi@contoh.com", "Budi", null, null);
        _users.FindWithRolesAsync(user.PublicId, Arg.Any<CancellationToken>()).Returns(user);
        _roles.FindManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);

        await FluentActions.Awaiting(() => SetRolesHandler().Handle(
                new SetUserRolesCommand(user.PublicId, [new UserRoleInput(Guid.NewGuid(), "global", null)]), default))
            .Should().ThrowAsync<NotFoundException>();

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetUserRoles_scope_departemen_tidak_dikenal_menghasilkan_NotFound()
    {
        var user = UserEntity.Create(1, "budi@contoh.com", "Budi", null, null);
        var role = Role.Create(1, "KUSTOM", "Kustom", null, []);
        _users.FindWithRolesAsync(user.PublicId, Arg.Any<CancellationToken>()).Returns(user);
        _roles.FindManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([role]);
        _departments.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Department?)null);

        await FluentActions.Awaiting(() => SetRolesHandler().Handle(
                new SetUserRolesCommand(user.PublicId, [new UserRoleInput(role.PublicId, "department", Guid.NewGuid())]), default))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task SetUserRoles_user_tidak_dikenal_menghasilkan_NotFound()
    {
        _users.FindWithRolesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((UserEntity?)null);

        await FluentActions.Awaiting(() => SetRolesHandler().Handle(new SetUserRolesCommand(Guid.NewGuid(), []), default))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task SetUserRoles_validator_menolak_scope_tidak_dikenal()
    {
        var validator = new SetUserRolesCommandValidator();

        (await validator.ValidateAsync(new SetUserRolesCommand(Guid.NewGuid(), [new UserRoleInput(Guid.NewGuid(), "galaksi", null)])))
            .IsValid.Should().BeFalse();
        (await validator.ValidateAsync(new SetUserRolesCommand(Guid.NewGuid(), [new UserRoleInput(Guid.NewGuid(), "Global", null)])))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task CreateRole_permission_tidak_dikenal_ditolak_sebelum_menyimpan()
    {
        _roles.CodeExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        _permissions.FindByCodesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns([]);
        var handler = new CreateRoleCommandHandler(_roles, _permissions, Substitute.For<IRoleReadService>(), _unitOfWork, _currentUser);

        await FluentActions.Awaiting(() => handler.Handle(new CreateRoleCommand("KUSTOM", "Kustom", null, ["tidak.ada"]), default))
            .Should().ThrowAsync<DomainException>().WithMessage("*tidak.ada*");

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteRole_ditolak_bila_masih_ditugaskan_ke_user()
    {
        var role = Role.Create(1, "KUSTOM", "Kustom", null, []);
        _roles.FindAsync(role.PublicId, Arg.Any<CancellationToken>()).Returns(role);
        _roles.HasAssignmentsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(true);

        await FluentActions.Awaiting(() => new DeleteRoleCommandHandler(_roles, _unitOfWork).Handle(new DeleteRoleCommand(role.PublicId, 0), default))
            .Should().ThrowAsync<DomainException>();

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
