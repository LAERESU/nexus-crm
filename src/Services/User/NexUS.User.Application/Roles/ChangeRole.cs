using FluentValidation;
using MediatR;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.User.Application.Abstractions;
using NexUS.User.Application.Common;
using NexUS.User.Domain.Roles;

namespace NexUS.User.Application.Roles;

public sealed record UpdateRoleCommand(Guid Id, string Name, string? Description, long ExpectedRowVersion) : IRequest<RoleDto>;

public sealed class UpdateRoleCommandValidator : AbstractValidator<UpdateRoleCommand>
{
    public UpdateRoleCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Role.NameMaxLength);
        RuleFor(x => x.Description).MaximumLength(Role.DescriptionMaxLength);
        RuleFor(x => x.ExpectedRowVersion).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateRoleCommandHandler(IRoleRepository roles, IRoleReadService readService, IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateRoleCommand, RoleDto>
{
    public async Task<RoleDto> Handle(UpdateRoleCommand request, CancellationToken ct)
    {
        var role = await roles.FindAsync(request.Id, ct) ?? throw new NotFoundException("Role tidak ditemukan.");

        role.Update(request.Name, request.Description);
        unitOfWork.ExpectRowVersion(role, request.ExpectedRowVersion);
        await unitOfWork.SaveChangesAsync(ct);

        return await readService.GetAsync(role.PublicId, ct)
            ?? throw new InvalidOperationException("Role yang baru diubah tidak terbaca.");
    }
}

public sealed record SetRolePermissionsCommand(Guid Id, IReadOnlyList<string> Permissions, long ExpectedRowVersion) : IRequest<RoleDto>;

public sealed class SetRolePermissionsCommandValidator : AbstractValidator<SetRolePermissionsCommand>
{
    public SetRolePermissionsCommandValidator()
    {
        RuleFor(x => x.Permissions).NotNull().Must(p => p.Count <= 500).WithMessage("Terlalu banyak permission.");
        RuleForEach(x => x.Permissions).NotEmpty().MaximumLength(Permission.CodeMaxLength);
        RuleFor(x => x.ExpectedRowVersion).GreaterThanOrEqualTo(0);
    }
}

public sealed class SetRolePermissionsCommandHandler(
    IRoleRepository roles, IPermissionRepository permissions, IRoleReadService readService, IUnitOfWork unitOfWork)
    : IRequestHandler<SetRolePermissionsCommand, RoleDto>
{
    public async Task<RoleDto> Handle(SetRolePermissionsCommand request, CancellationToken ct)
    {
        var role = await roles.FindAsync(request.Id, ct) ?? throw new NotFoundException("Role tidak ditemukan.");

        role.SetPermissions(await PermissionLookup.ResolveAsync(permissions, request.Permissions, ct));
        unitOfWork.ExpectRowVersion(role, request.ExpectedRowVersion);
        await unitOfWork.SaveChangesAsync(ct); // juga menyimpan event RoleChanged ke outbox

        return await readService.GetAsync(role.PublicId, ct)
            ?? throw new InvalidOperationException("Role yang baru diubah tidak terbaca.");
    }
}

public sealed record DeleteRoleCommand(Guid Id, long ExpectedRowVersion) : IRequest;

public sealed class DeleteRoleCommandHandler(IRoleRepository roles, IUnitOfWork unitOfWork) : IRequestHandler<DeleteRoleCommand>
{
    public async Task Handle(DeleteRoleCommand request, CancellationToken ct)
    {
        var role = await roles.FindAsync(request.Id, ct) ?? throw new NotFoundException("Role tidak ditemukan.");

        role.Delete(); // menolak role sistem
        if (await roles.HasAssignmentsAsync(role.Id, ct))
            throw new DomainException("Role masih ditugaskan ke user; cabut penugasannya lebih dulu.");

        unitOfWork.ExpectRowVersion(role, request.ExpectedRowVersion);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
