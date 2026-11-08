using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexUS.BuildingBlocks.Common.Results;
using NexUS.User.Application.Roles;

namespace NexUS.User.Api.Controllers;

public sealed record CreateRoleRequest(string Code, string Name, string? Description, List<string>? Permissions);

public sealed record UpdateRoleRequest(string Name, string? Description);

public sealed record SetRolePermissionsRequest(List<string>? Permissions);

[ApiController]
[Route("api/v1/roles")]
public sealed class RolesController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Policies.RoleRead)]
    [ProducesResponseType<PagedResult<RoleDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<RoleDto>> List(
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int size = PagedResult<RoleDto>.DefaultSize,
        CancellationToken ct = default) =>
        mediator.Send(new ListRolesQuery(search, page, size), ct);

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.RoleRead)]
    [ProducesResponseType<RoleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<RoleDto> Get(Guid id, CancellationToken ct) => WithEtag(await mediator.Send(new GetRoleQuery(id), ct));

    /// <summary>Membuat role kustom. Kode berformat HURUF_BESAR_DAN_GARIS_BAWAH dan unik per tenant.</summary>
    [HttpPost]
    [Authorize(Policy = Policies.RoleManage)]
    [ProducesResponseType<RoleDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<CreatedAtActionResult> Create(CreateRoleRequest request, CancellationToken ct)
    {
        var role = await mediator.Send(
            new CreateRoleCommand(request.Code, request.Name, request.Description, request.Permissions ?? []), ct);
        IfMatchHeader.Write(Response, role.RowVersion);
        return CreatedAtAction(nameof(Get), new { id = role.Id }, role);
    }

    /// <summary>Mengubah nama dan deskripsi role kustom. Role sistem ditolak (422). Wajib header <c>If-Match</c>.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.RoleManage)]
    [ProducesResponseType<RoleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<RoleDto> Update(Guid id, UpdateRoleRequest request, CancellationToken ct) =>
        WithEtag(await mediator.Send(new UpdateRoleCommand(id, request.Name, request.Description, IfMatchHeader.Require(Request)), ct));

    /// <summary>Menghapus (soft delete) role kustom yang tidak ditugaskan ke siapa pun. Role sistem ditolak (422).</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.RoleManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await mediator.Send(new DeleteRoleCommand(id, IfMatchHeader.Require(Request)), ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/permissions")]
    [Authorize(Policy = Policies.RoleRead)]
    [ProducesResponseType<IReadOnlyList<string>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<string>> GetPermissions(Guid id, CancellationToken ct) =>
        (await mediator.Send(new GetRoleQuery(id), ct)).Permissions;

    /// <summary>
    /// Mengganti seluruh permission role (daftar kode <c>modul.aksi</c>). Role sistem ditolak (422); kode yang tidak
    /// ada di katalog ditolak (422). Wajib header <c>If-Match</c>.
    /// </summary>
    [HttpPut("{id:guid}/permissions")]
    [Authorize(Policy = Policies.RoleManage)]
    [ProducesResponseType<RoleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<RoleDto> SetPermissions(Guid id, SetRolePermissionsRequest request, CancellationToken ct) =>
        WithEtag(await mediator.Send(new SetRolePermissionsCommand(id, request.Permissions ?? [], IfMatchHeader.Require(Request)), ct));

    private RoleDto WithEtag(RoleDto role)
    {
        IfMatchHeader.Write(Response, role.RowVersion);
        return role;
    }
}

[ApiController]
[Route("api/v1/permissions")]
public sealed class PermissionsController(ISender mediator) : ControllerBase
{
    /// <summary>Katalog seluruh permission yang dikenal sistem.</summary>
    [HttpGet]
    [Authorize(Policy = Policies.RoleRead)]
    [ProducesResponseType<IReadOnlyList<PermissionDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<PermissionDto>> List(CancellationToken ct) => mediator.Send(new ListPermissionsQuery(), ct);
}
