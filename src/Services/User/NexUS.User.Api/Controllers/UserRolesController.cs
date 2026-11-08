using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexUS.User.Application.Roles;
using NexUS.User.Application.Users;

namespace NexUS.User.Api.Controllers;

/// <param name="ScopeType"><c>global</c> (tanpa scopeId), <c>department</c>, atau <c>project</c> (scopeId wajib).</param>
public sealed record UserRoleRequest(Guid RoleId, string ScopeType, Guid? ScopeId);

public sealed record SetUserRolesRequest(List<UserRoleRequest>? Roles);

[ApiController]
[Route("api/v1/users/{id:guid}")]
public sealed class UserRolesController(ISender mediator) : ControllerBase
{
    /// <summary>
    /// Mengganti seluruh penugasan role user (penugasan lama yang tidak disebut dicabut). Perubahan langsung
    /// tercermin pada <c>GET permissions</c> dan menerbitkan event <c>RoleChanged</c>.
    /// </summary>
    [HttpPut("roles")]
    [Authorize(Policy = Policies.RoleManage)]
    [ProducesResponseType<UserPermissionsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<UserPermissionsDto> SetRoles(Guid id, SetUserRolesRequest request, CancellationToken ct) =>
        mediator.Send(new SetUserRolesCommand(
            id, (request.Roles ?? []).Select(r => new UserRoleInput(r.RoleId, r.ScopeType, r.ScopeId)).ToList()), ct);

    /// <summary>
    /// Akses efektif user: role beserta scope-nya dan daftar permission (dihitung langsung dari database, tanpa
    /// cache). Dipakai Auth/Gateway; user biasa hanya boleh membaca miliknya sendiri, selain itu 403.
    /// </summary>
    [HttpGet("permissions")]
    [Authorize(Policy = Policies.UserRead)]
    [ProducesResponseType<UserPermissionsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<UserPermissionsDto> GetPermissions(Guid id, CancellationToken ct) =>
        mediator.Send(new GetUserPermissionsQuery(id), ct);
}
