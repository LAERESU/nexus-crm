using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexUS.BuildingBlocks.Common.Results;
using NexUS.User.Application.Users;
using Roles = NexUS.User.Application.Roles;

namespace NexUS.User.Api.Controllers;

public sealed record CreateUserRequest(string Email, string FullName, string? JobTitle, Guid? DepartmentId);

public sealed record UpdateUserRequest(string FullName, string? JobTitle, Guid? DepartmentId);

public sealed record SetUserStatusRequest(string Status);

[ApiController]
[Route("api/v1/users")]
public sealed class UsersController(ISender mediator) : ControllerBase
{
    /// <summary>Daftar user tenant ini, dengan pencarian, filter departemen/status, dan paging.</summary>
    [HttpGet]
    [Authorize(Policy = Policies.UserRead)]
    [ProducesResponseType<PagedResult<UserDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<UserDto>> List(
        [FromQuery] string? search, [FromQuery] Guid? departmentId, [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int size = PagedResult<UserDto>.DefaultSize, CancellationToken ct = default) =>
        mediator.Send(new ListUsersQuery(search, departmentId, status, page, size), ct);

    /// <summary>Profil user yang sedang login.</summary>
    [HttpGet("me")]
    [Authorize(Policy = Policies.Authenticated)]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<UserDto> Me(CancellationToken ct) => WithEtag(await mediator.Send(new GetMeQuery(), ct));

    /// <summary>
    /// Akses efektif user yang sedang login: role beserta scope-nya dan daftar permission. Dipakai aplikasi web untuk
    /// tampilan (menu, route, tombol); <c>GET {id}/permissions</c> tetap khusus antar service dan diblokir di Gateway.
    /// </summary>
    [HttpGet("me/permissions")]
    [Authorize(Policy = Policies.Authenticated)]
    [ProducesResponseType<Roles.UserPermissionsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<Roles.UserPermissionsDto> MyPermissions(CancellationToken ct) => mediator.Send(new GetMyPermissionsQuery(), ct);

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.UserRead)]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<UserDto> Get(Guid id, CancellationToken ct) => WithEtag(await mediator.Send(new GetUserQuery(id), ct));

    /// <summary>Membuat user baru (admin). Email unik per tenant tanpa membedakan huruf besar/kecil.</summary>
    [HttpPost]
    [Authorize(Policy = Policies.UserManage)]
    [ProducesResponseType<UserDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<CreatedAtActionResult> Create(CreateUserRequest request, CancellationToken ct)
    {
        var user = await mediator.Send(new CreateUserCommand(request.Email, request.FullName, request.JobTitle, request.DepartmentId), ct);
        IfMatchHeader.Write(Response, user.RowVersion);
        return CreatedAtAction(nameof(Get), new { id = user.Id }, user);
    }

    /// <summary>Mengubah profil user. Wajib mengirim header <c>If-Match</c> berisi rowVersion terakhir.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.UserManage)]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<UserDto> Update(Guid id, UpdateUserRequest request, CancellationToken ct) =>
        WithEtag(await mediator.Send(
            new UpdateUserCommand(id, request.FullName, request.JobTitle, request.DepartmentId, IfMatchHeader.Require(Request)), ct));

    /// <summary>Mengaktifkan atau menonaktifkan user (<c>active</c> / <c>inactive</c>).</summary>
    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = Policies.UserManage)]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<UserDto> SetStatus(Guid id, SetUserStatusRequest request, CancellationToken ct) =>
        WithEtag(await mediator.Send(new SetUserStatusCommand(id, request.Status, IfMatchHeader.Require(Request)), ct));

    private UserDto WithEtag(UserDto user)
    {
        IfMatchHeader.Write(Response, user.RowVersion);
        return user;
    }
}
