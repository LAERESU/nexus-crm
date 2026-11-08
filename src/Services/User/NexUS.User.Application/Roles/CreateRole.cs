using FluentValidation;
using MediatR;
using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.User.Application.Abstractions;
using NexUS.User.Application.Common;
using NexUS.User.Domain.Roles;

namespace NexUS.User.Application.Roles;

public sealed record CreateRoleCommand(string Code, string Name, string? Description, IReadOnlyList<string> Permissions) : IRequest<RoleDto>;

public sealed class CreateRoleCommandValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(Role.CodeMaxLength);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Role.NameMaxLength);
        RuleFor(x => x.Description).MaximumLength(Role.DescriptionMaxLength);
        RuleFor(x => x.Permissions).NotNull().Must(p => p.Count <= 500).WithMessage("Terlalu banyak permission.");
        RuleForEach(x => x.Permissions).NotEmpty().MaximumLength(Permission.CodeMaxLength);
    }
}

public sealed class CreateRoleCommandHandler(
    IRoleRepository roles,
    IPermissionRepository permissions,
    IRoleReadService readService,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : IRequestHandler<CreateRoleCommand, RoleDto>
{
    public async Task<RoleDto> Handle(CreateRoleCommand request, CancellationToken ct)
    {
        var code = Role.NormalizeCode(request.Code);
        if (DefaultRoleCatalog.Roles.Any(r => r.Code == code))
            throw new DomainException("Kode role dicadangkan untuk role bawaan sistem.");
        if (await roles.CodeExistsAsync(code, ct))
            throw new DomainException("Kode role sudah dipakai di tenant ini.");

        var granted = await PermissionLookup.ResolveAsync(permissions, request.Permissions, ct);
        var role = Role.Create(currentUser.TenantId, code, request.Name, request.Description, granted);

        await roles.AddAsync(role, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return await readService.GetAsync(role.PublicId, ct)
            ?? throw new InvalidOperationException("Role yang baru dibuat tidak terbaca.");
    }
}

internal static class PermissionLookup
{
    /// <summary>Mengubah kode permission menjadi entitas; kode yang tidak ada di katalog ditolak (422).</summary>
    public static async Task<IReadOnlyCollection<Permission>> ResolveAsync(
        IPermissionRepository permissions, IReadOnlyCollection<string> codes, CancellationToken ct)
    {
        var normalized = codes.Select(c => c.Trim().ToLowerInvariant()).Distinct().ToList();
        var found = await permissions.FindByCodesAsync(normalized, ct);

        var unknown = normalized.Except(found.Select(p => p.Code)).ToList();
        if (unknown.Count > 0)
            throw new DomainException($"Permission tidak dikenal: {string.Join(", ", unknown)}.");

        return found.ToList();
    }
}
