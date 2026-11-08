using FluentAssertions;
using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.BuildingBlocks.Common.Results;
using NexUS.User.Application.Abstractions;
using NexUS.User.Application.Common;
using NexUS.User.Application.Users;
using NexUS.User.Domain.Departments;
using NSubstitute;
using UserEntity = NexUS.User.Domain.Users.User;

namespace NexUS.User.UnitTests;

public class UserHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IUserReadService _readService = Substitute.For<IUserReadService>();
    private readonly IDepartmentRepository _departments = Substitute.For<IDepartmentRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

    public UserHandlerTests() => _currentUser.TenantId.Returns(7);

    private static UserDto Dto(Guid id) => new(id, "budi@contoh.com", "Budi", null, "active", null, null, 0, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Create_menolak_email_yang_sudah_dipakai_di_tenant()
    {
        _users.EmailExistsAsync("budi@contoh.com", Arg.Any<CancellationToken>()).Returns(true);
        var handler = new CreateUserCommandHandler(_users, _readService, _departments, _unitOfWork, _currentUser);

        // Email dinormalisasi sebelum dicek, jadi perbedaan huruf besar/kecil tetap terdeteksi sebagai duplikat.
        await FluentActions.Awaiting(() => handler.Handle(new CreateUserCommand("BUDI@Contoh.com", "Budi", null, null), default))
            .Should().ThrowAsync<DomainException>();

        await _users.DidNotReceive().AddAsync(Arg.Any<UserEntity>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_menyimpan_user_pada_tenant_pemanggil()
    {
        UserEntity? added = null;
        await _users.AddAsync(Arg.Do<UserEntity>(u => added = u), Arg.Any<CancellationToken>());
        _readService.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(ci => Dto(ci.Arg<Guid>()));
        var handler = new CreateUserCommandHandler(_users, _readService, _departments, _unitOfWork, _currentUser);

        await handler.Handle(new CreateUserCommand("budi@contoh.com", "Budi", null, null), default);

        added.Should().NotBeNull();
        added!.TenantId.Should().Be(7);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_dengan_departemen_tidak_dikenal_menghasilkan_NotFound()
    {
        _departments.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Department?)null);
        var handler = new CreateUserCommandHandler(_users, _readService, _departments, _unitOfWork, _currentUser);

        await FluentActions.Awaiting(() => handler.Handle(new CreateUserCommand("budi@contoh.com", "Budi", null, Guid.NewGuid()), default))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task SetStatus_menolak_menonaktifkan_akun_sendiri()
    {
        var user = UserEntity.Create(7, "admin@contoh.com", "Admin", null, null);
        _users.FindAsync(user.PublicId, Arg.Any<CancellationToken>()).Returns(user);
        _currentUser.UserId.Returns(user.PublicId);
        var handler = new SetUserStatusCommandHandler(_users, _readService, _unitOfWork, _currentUser);

        await FluentActions.Awaiting(() => handler.Handle(new SetUserStatusCommand(user.PublicId, "inactive", 0), default))
            .Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task SetStatus_memakai_versi_dari_klien_untuk_cek_konkurensi()
    {
        var user = UserEntity.Create(7, "budi@contoh.com", "Budi", null, null);
        _users.FindAsync(user.PublicId, Arg.Any<CancellationToken>()).Returns(user);
        _readService.GetAsync(user.PublicId, Arg.Any<CancellationToken>()).Returns(Dto(user.PublicId));
        var handler = new SetUserStatusCommandHandler(_users, _readService, _unitOfWork, _currentUser);

        await handler.Handle(new SetUserStatusCommand(user.PublicId, "inactive", 4), default);

        _unitOfWork.Received(1).ExpectRowVersion(user, 4);
    }

    [Fact]
    public async Task ListUsers_validator_menolak_size_di_atas_100_dan_status_asing()
    {
        var validator = new ListUsersQueryValidator();

        (await validator.ValidateAsync(new ListUsersQuery(null, null, null, 1, 101))).IsValid.Should().BeFalse();
        (await validator.ValidateAsync(new ListUsersQuery(null, null, "banned", 1, 20))).IsValid.Should().BeFalse();
        (await validator.ValidateAsync(new ListUsersQuery(null, null, null, int.MaxValue, 100))).IsValid.Should().BeFalse(); // overflow offset
        (await validator.ValidateAsync(new ListUsersQuery("budi", null, "Active", 1, PagedResult<UserDto>.MaxSize))).IsValid.Should().BeTrue();
    }
}
