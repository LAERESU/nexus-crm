using FluentValidation;
using MediatR;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.BuildingBlocks.Common.Results;
using NexUS.User.Application.Abstractions;

namespace NexUS.User.Application.Roles;

public sealed record ListRolesQuery(string? Search, int Page = 1, int Size = PagedResult<RoleDto>.DefaultSize)
    : IRequest<PagedResult<RoleDto>>;

public sealed class ListRolesQueryValidator : AbstractValidator<ListRolesQuery>
{
    public ListRolesQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.Page).InclusiveBetween(1, 100_000);
        RuleFor(x => x.Size).InclusiveBetween(1, PagedResult<RoleDto>.MaxSize);
    }
}

public sealed class ListRolesQueryHandler(IRoleReadService roles) : IRequestHandler<ListRolesQuery, PagedResult<RoleDto>>
{
    public Task<PagedResult<RoleDto>> Handle(ListRolesQuery request, CancellationToken ct) =>
        roles.ListAsync(request.Search?.Trim(), request.Page, request.Size, ct);
}

public sealed record GetRoleQuery(Guid Id) : IRequest<RoleDto>;

public sealed class GetRoleQueryHandler(IRoleReadService roles) : IRequestHandler<GetRoleQuery, RoleDto>
{
    public async Task<RoleDto> Handle(GetRoleQuery request, CancellationToken ct) =>
        await roles.GetAsync(request.Id, ct) ?? throw new NotFoundException("Role tidak ditemukan.");
}

/// <summary>Katalog permission, dipakai matriks permission di UI (NX-023).</summary>
public sealed record ListPermissionsQuery : IRequest<IReadOnlyList<PermissionDto>>;

public sealed class ListPermissionsQueryHandler(IPermissionRepository permissions)
    : IRequestHandler<ListPermissionsQuery, IReadOnlyList<PermissionDto>>
{
    public async Task<IReadOnlyList<PermissionDto>> Handle(ListPermissionsQuery request, CancellationToken ct) =>
        (await permissions.ListAsync(ct)).OrderBy(p => p.Module).ThenBy(p => p.Code)
            .Select(p => new PermissionDto(p.Code, p.Module, p.Description)).ToList();
}
