using FluentValidation;
using MediatR;
using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.User.Application.Abstractions;
using NexUS.User.Application.Common;
using NexUS.User.Application.Roles;
using NexUS.User.Domain.Users;
using UserEntity = NexUS.User.Domain.Users.User;

namespace NexUS.User.Application.Users;

public sealed record UserRoleInput(Guid RoleId, string ScopeType, Guid? ScopeId);

public sealed record SetUserRolesCommand(Guid UserId, IReadOnlyList<UserRoleInput> Roles) : IRequest<UserPermissionsDto>;

public sealed class SetUserRolesCommandValidator : AbstractValidator<SetUserRolesCommand>
{
    public SetUserRolesCommandValidator()
    {
        RuleFor(x => x.Roles).NotNull().Must(r => r.Count <= UserEntity.MaxRoleAssignments)
            .WithMessage($"Satu user maksimal memiliki {UserEntity.MaxRoleAssignments} penugasan role.");
        RuleForEach(x => x.Roles).ChildRules(role =>
        {
            role.RuleFor(r => r.RoleId).NotEmpty();
            role.RuleFor(r => r.ScopeType).NotEmpty()
                .Must(s => ScopeTypeNames.TryParse(s, out _)).WithMessage("scopeType harus 'global', 'department', atau 'project'.");
        });
    }
}

public sealed class SetUserRolesCommandHandler(
    IUserRepository users,
    IRoleRepository roles,
    IDepartmentRepository departments,
    IUserPermissionReadService permissionService,
    IUnitOfWork unitOfWork) : IRequestHandler<SetUserRolesCommand, UserPermissionsDto>
{
    public async Task<UserPermissionsDto> Handle(SetUserRolesCommand request, CancellationToken ct)
    {
        var user = await users.FindWithRolesAsync(request.UserId, ct) ?? throw new NotFoundException("User tidak ditemukan.");

        var requestedRoles = await roles.FindManyAsync(request.Roles.Select(r => r.RoleId).Distinct().ToList(), ct);
        var assignments = new List<RoleAssignment>();
        foreach (var input in request.Roles)
        {
            var role = requestedRoles.FirstOrDefault(r => r.PublicId == input.RoleId)
                ?? throw new NotFoundException($"Role {input.RoleId} tidak ditemukan.");
            ScopeTypeNames.TryParse(input.ScopeType, out var scopeType);

            // Scope departemen harus departemen tenant ini. Scope proyek milik layanan Project, jadi hanya
            // dicatat apa adanya; keanggotaan proyek tetap diperiksa layanan tersebut.
            if (scopeType == RoleScopeType.Department && input.ScopeId is { } departmentId
                && await departments.FindAsync(departmentId, ct) is null)
                throw new NotFoundException($"Departemen {departmentId} tidak ditemukan.");

            assignments.Add(new RoleAssignment(role.Id, scopeType, input.ScopeId));
        }

        user.SetRoles(assignments);
        await unitOfWork.SaveChangesAsync(ct); // juga menyimpan event RoleChanged ke outbox

        return await permissionService.GetAsync(user.PublicId, ct)
            ?? throw new InvalidOperationException("Permission user yang baru diubah tidak terbaca.");
    }
}

public sealed record GetUserPermissionsQuery(Guid UserId) : IRequest<UserPermissionsDto>;

public sealed class GetUserPermissionsQueryHandler(IUserPermissionReadService permissionService, ICurrentUser currentUser)
    : IRequestHandler<GetUserPermissionsQuery, UserPermissionsDto>
{
    // Token sementara membawa role "admin" (ADR-0006); NX-020 menggantinya dengan pemeriksaan permission.
    private const string AdminRole = "admin";

    public async Task<UserPermissionsDto> Handle(GetUserPermissionsQuery request, CancellationToken ct)
    {
        // Kepemilikan objek: hak akses orang lain hanya boleh dibaca admin; user biasa hanya miliknya sendiri.
        if (request.UserId != currentUser.UserId && !currentUser.Roles.Contains(AdminRole))
            throw new ForbiddenException("Anda hanya dapat melihat permission akun sendiri.");

        return await permissionService.GetAsync(request.UserId, ct) ?? throw new NotFoundException("User tidak ditemukan.");
    }
}
