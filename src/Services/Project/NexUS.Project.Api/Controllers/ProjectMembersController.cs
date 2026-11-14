using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexUS.BuildingBlocks.Common.Results;
using NexUS.Project.Application.Projects;

namespace NexUS.Project.Api.Controllers;

/// <param name="Role"><c>manager</c>, <c>member</c>, atau <c>viewer</c>.</param>
public sealed record AddProjectMemberRequest(Guid UserId, string Role);

/// <param name="Role"><c>manager</c>, <c>member</c>, atau <c>viewer</c>.</param>
public sealed record ChangeProjectMemberRoleRequest(string Role);

/// <summary>
/// Anggota proyek. Membaca butuh keanggotaan proyek; mengubah hanya boleh manager proyek itu atau pemegang
/// <c>project.all.read</c> (admin tenant), dan keduanya tetap butuh permission <c>project.member.manage</c>.
/// </summary>
[ApiController]
[Route("api/v1/projects/{projectId:guid}/members")]
public sealed class ProjectMembersController(ISender mediator) : ControllerBase
{
    /// <summary>Daftar anggota dengan paging, urut peran lalu nama.</summary>
    [HttpGet]
    [Authorize(Policy = Policies.ProjectRead)]
    [ProducesResponseType<PagedResult<ProjectMemberDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<PagedResult<ProjectMemberDto>> List(
        Guid projectId, [FromQuery] int page = 1, [FromQuery] int size = PagedResult<ProjectMemberDto>.DefaultSize,
        CancellationToken ct = default) =>
        mediator.Send(new ListProjectMembersQuery(projectId, page, size), ct);

    /// <summary>Menambah anggota. User yang sudah anggota atau diketahui nonaktif ditolak (422).</summary>
    [HttpPost]
    [Authorize(Policy = Policies.ProjectMemberManage)]
    [ProducesResponseType<ProjectMemberDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<CreatedAtActionResult> Add(Guid projectId, AddProjectMemberRequest request, CancellationToken ct)
    {
        var member = await mediator.Send(new AddProjectMemberCommand(projectId, request.UserId, request.Role), ct);
        return CreatedAtAction(nameof(List), new { projectId }, member);
    }

    /// <summary>Mengganti peran anggota. Manager terakhir tidak bisa diturunkan (422).</summary>
    [HttpPut("{userId:guid}")]
    [Authorize(Policy = Policies.ProjectMemberManage)]
    [ProducesResponseType<ProjectMemberDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<ProjectMemberDto> ChangeRole(Guid projectId, Guid userId, ChangeProjectMemberRoleRequest request, CancellationToken ct) =>
        mediator.Send(new ChangeProjectMemberRoleCommand(projectId, userId, request.Role), ct);

    /// <summary>Mengeluarkan anggota. Manager terakhir tidak bisa dikeluarkan (422).</summary>
    [HttpDelete("{userId:guid}")]
    [Authorize(Policy = Policies.ProjectMemberManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Remove(Guid projectId, Guid userId, CancellationToken ct)
    {
        await mediator.Send(new RemoveProjectMemberCommand(projectId, userId), ct);
        return NoContent();
    }
}
