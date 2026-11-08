using FluentValidation;
using MediatR;
using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.User.Application.Abstractions;
using NexUS.User.Application.Common;
using UserEntity = NexUS.User.Domain.Users.User;

namespace NexUS.User.Application.Users;

public sealed record CreateUserCommand(string Email, string FullName, string? JobTitle, Guid? DepartmentId) : IRequest<UserDto>;

public sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(UserEntity.EmailMaxLength).EmailAddress();
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(UserEntity.FullNameMaxLength);
        RuleFor(x => x.JobTitle).MaximumLength(UserEntity.JobTitleMaxLength);
    }
}

public sealed class CreateUserCommandHandler(
    IUserRepository users,
    IUserReadService readService,
    IDepartmentRepository departments,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : IRequestHandler<CreateUserCommand, UserDto>
{
    public async Task<UserDto> Handle(CreateUserCommand request, CancellationToken ct)
    {
        var email = UserEntity.NormalizeEmail(request.Email);
        if (await users.EmailExistsAsync(email, ct))
            throw new DomainException("Email sudah dipakai user lain di tenant ini.");

        long? departmentId = null;
        if (request.DepartmentId is { } publicDepartmentId)
        {
            var department = await departments.FindAsync(publicDepartmentId, ct)
                ?? throw new NotFoundException("Departemen tidak ditemukan.");
            departmentId = department.Id;
        }

        var user = UserEntity.Create(currentUser.TenantId, email, request.FullName, request.JobTitle, departmentId);
        await users.AddAsync(user, ct);
        await unitOfWork.SaveChangesAsync(ct); // juga menyimpan event UserCreated ke outbox

        return await readService.GetAsync(user.PublicId, ct)
            ?? throw new InvalidOperationException("User yang baru dibuat tidak terbaca.");
    }
}
