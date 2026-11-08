using FluentValidation;
using MediatR;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.User.Application.Abstractions;
using NexUS.User.Application.Common;
using UserEntity = NexUS.User.Domain.Users.User;

namespace NexUS.User.Application.Users;

public sealed record UpdateUserCommand(Guid Id, string FullName, string? JobTitle, Guid? DepartmentId, long ExpectedRowVersion)
    : IRequest<UserDto>;

public sealed class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(UserEntity.FullNameMaxLength);
        RuleFor(x => x.JobTitle).MaximumLength(UserEntity.JobTitleMaxLength);
        RuleFor(x => x.ExpectedRowVersion).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateUserCommandHandler(
    IUserRepository users,
    IUserReadService readService,
    IDepartmentRepository departments,
    IUnitOfWork unitOfWork) : IRequestHandler<UpdateUserCommand, UserDto>
{
    public async Task<UserDto> Handle(UpdateUserCommand request, CancellationToken ct)
    {
        var user = await users.FindAsync(request.Id, ct) ?? throw new NotFoundException("User tidak ditemukan.");

        long? departmentId = null;
        if (request.DepartmentId is { } publicDepartmentId)
        {
            var department = await departments.FindAsync(publicDepartmentId, ct)
                ?? throw new NotFoundException("Departemen tidak ditemukan.");
            departmentId = department.Id;
        }

        user.UpdateProfile(request.FullName, request.JobTitle, departmentId);
        unitOfWork.ExpectRowVersion(user, request.ExpectedRowVersion);
        await unitOfWork.SaveChangesAsync(ct);

        return await readService.GetAsync(user.PublicId, ct)
            ?? throw new InvalidOperationException("User yang baru diubah tidak terbaca.");
    }
}
