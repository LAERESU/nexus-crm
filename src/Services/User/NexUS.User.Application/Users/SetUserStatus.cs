using FluentValidation;
using MediatR;
using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.User.Application.Abstractions;
using NexUS.User.Application.Common;

namespace NexUS.User.Application.Users;

public sealed record SetUserStatusCommand(Guid Id, string Status, long ExpectedRowVersion) : IRequest<UserDto>;

public sealed class SetUserStatusCommandValidator : AbstractValidator<SetUserStatusCommand>
{
    public SetUserStatusCommandValidator()
    {
        RuleFor(x => x.Status)
            .NotEmpty()
            .Must(s => UserStatusNames.TryParse(s, out _))
            .WithMessage("Status harus 'active' atau 'inactive'.");
        RuleFor(x => x.ExpectedRowVersion).GreaterThanOrEqualTo(0);
    }
}

public sealed class SetUserStatusCommandHandler(
    IUserRepository users,
    IUserReadService readService,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : IRequestHandler<SetUserStatusCommand, UserDto>
{
    public async Task<UserDto> Handle(SetUserStatusCommand request, CancellationToken ct)
    {
        var user = await users.FindAsync(request.Id, ct) ?? throw new NotFoundException("User tidak ditemukan.");
        UserStatusNames.TryParse(request.Status, out var status);

        if (status == Domain.Users.UserStatus.Inactive)
        {
            if (user.PublicId == currentUser.UserId)
                throw new DomainException("Anda tidak dapat menonaktifkan akun sendiri.");
            user.Deactivate();
        }
        else
        {
            user.Activate();
        }

        unitOfWork.ExpectRowVersion(user, request.ExpectedRowVersion);
        await unitOfWork.SaveChangesAsync(ct);

        return await readService.GetAsync(user.PublicId, ct)
            ?? throw new InvalidOperationException("User yang baru diubah tidak terbaca.");
    }
}
