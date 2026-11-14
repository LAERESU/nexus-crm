using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexUS.BuildingBlocks.Common.Results;
using NexUS.Project.Application.Milestones;

namespace NexUS.Project.Api.Controllers;

/// <param name="DueDate">Harus di dalam rentang tanggal proyek.</param>
public sealed record CreateMilestoneRequest(string Name, string? Description, DateOnly DueDate);

/// <param name="Status"><c>open</c> atau <c>completed</c>.</param>
public sealed record UpdateMilestoneRequest(string Name, string? Description, DateOnly DueDate, string Status);

/// <summary>
/// Milestone proyek. Membaca butuh keanggotaan proyek; membuat dan mengubah hanya manager proyek itu (atau admin
/// tenant), dengan permission <c>project.update</c>.
/// </summary>
[ApiController]
[Route("api/v1/projects/{projectId:guid}/milestones")]
public sealed class MilestonesController(ISender mediator) : ControllerBase
{
    /// <summary>Daftar milestone dengan paging, urut tanggal jatuh tempo.</summary>
    [HttpGet]
    [Authorize(Policy = Policies.ProjectRead)]
    [ProducesResponseType<PagedResult<MilestoneDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<PagedResult<MilestoneDto>> List(
        Guid projectId, [FromQuery] int page = 1, [FromQuery] int size = PagedResult<MilestoneDto>.DefaultSize,
        CancellationToken ct = default) =>
        mediator.Send(new ListMilestonesQuery(projectId, page, size), ct);

    /// <summary>Membuat milestone berstatus <c>open</c>. Tanggal di luar rentang proyek ditolak (422).</summary>
    [HttpPost]
    [Authorize(Policy = Policies.ProjectUpdate)]
    [ProducesResponseType<MilestoneDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<CreatedAtActionResult> Create(Guid projectId, CreateMilestoneRequest request, CancellationToken ct)
    {
        var milestone = await mediator.Send(new CreateMilestoneCommand(projectId, request.Name, request.Description, request.DueDate), ct);
        IfMatchHeader.Write(Response, milestone.RowVersion);
        return CreatedAtAction(nameof(List), new { projectId }, milestone);
    }

    /// <summary>Mengubah milestone. Wajib mengirim header <c>If-Match</c>; tanggal di luar rentang proyek ditolak (422).</summary>
    [HttpPut("{milestoneId:guid}")]
    [Authorize(Policy = Policies.ProjectUpdate)]
    [ProducesResponseType<MilestoneDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<MilestoneDto> Update(Guid projectId, Guid milestoneId, UpdateMilestoneRequest request, CancellationToken ct)
    {
        var milestone = await mediator.Send(new UpdateMilestoneCommand(
            projectId, milestoneId, request.Name, request.Description, request.DueDate, request.Status, IfMatchHeader.Require(Request)), ct);
        IfMatchHeader.Write(Response, milestone.RowVersion);
        return milestone;
    }
}
