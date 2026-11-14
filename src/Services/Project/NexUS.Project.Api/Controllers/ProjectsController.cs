using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexUS.BuildingBlocks.Common.Results;
using NexUS.Project.Application.Projects;

namespace NexUS.Project.Api.Controllers;

/// <param name="Priority"><c>low</c>, <c>medium</c> (bawaan), <c>high</c>, atau <c>urgent</c>.</param>
public sealed record CreateProjectRequest(
    string Code, string Name, string? Description, string? Priority, DateOnly? StartDate, DateOnly? EndDate, Guid? DepartmentId);

/// <param name="Status"><c>planning</c>, <c>active</c>, <c>on_hold</c>, atau <c>completed</c>.</param>
/// <param name="Priority"><c>low</c>, <c>medium</c>, <c>high</c>, atau <c>urgent</c>.</param>
public sealed record UpdateProjectRequest(
    string Name, string? Description, string Status, string Priority, DateOnly? StartDate, DateOnly? EndDate, Guid? DepartmentId);

[ApiController]
[Route("api/v1/projects")]
public sealed class ProjectsController(ISender mediator) : ControllerBase
{
    /// <summary>
    /// Daftar proyek yang diikuti user (semua proyek tenant bagi pemegang <c>project.all.read</c>) dengan pencarian (nama/kode), filter status, pemilik, dan departemen, serta paging.
    /// Tanpa <c>status</c>, proyek yang diarsipkan tidak ikut; <c>status=archived</c> menampilkan arsip.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = Policies.ProjectRead)]
    [ProducesResponseType<PagedResult<ProjectDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<ProjectDto>> List(
        [FromQuery] string? search, [FromQuery] string? status, [FromQuery] Guid? ownerId, [FromQuery] Guid? departmentId,
        [FromQuery] int page = 1, [FromQuery] int size = PagedResult<ProjectDto>.DefaultSize, CancellationToken ct = default) =>
        mediator.Send(new ListProjectsQuery(search, status, ownerId, departmentId, page, size), ct);

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ProjectRead)]
    [ProducesResponseType<ProjectDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ProjectDto> Get(Guid id, CancellationToken ct) => WithEtag(await mediator.Send(new GetProjectQuery(id), ct));

    /// <summary>Membuat proyek berstatus <c>planning</c>. Pembuat menjadi pemilik dan anggota berperan MANAGER.</summary>
    [HttpPost]
    [Authorize(Policy = Policies.ProjectCreate)]
    [ProducesResponseType<ProjectDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<CreatedAtActionResult> Create(CreateProjectRequest request, CancellationToken ct)
    {
        var project = await mediator.Send(new CreateProjectCommand(
            request.Code, request.Name, request.Description, request.Priority, request.StartDate, request.EndDate, request.DepartmentId), ct);
        WithEtag(project);
        return CreatedAtAction(nameof(Get), new { id = project.Id }, project);
    }

    /// <summary>Mengubah proyek. Wajib mengirim header <c>If-Match</c> berisi rowVersion terakhir.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.ProjectUpdate)]
    [ProducesResponseType<ProjectDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ProjectDto> Update(Guid id, UpdateProjectRequest request, CancellationToken ct) =>
        WithEtag(await mediator.Send(new UpdateProjectCommand(
            id, request.Name, request.Description, request.Status, request.Priority, request.StartDate, request.EndDate,
            request.DepartmentId, RequireIfMatch()), ct));

    /// <summary>Mengarsipkan proyek (status <c>archived</c>); proyek tidak lagi muncul di daftar bawaan.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.ProjectUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        await mediator.Send(new ArchiveProjectCommand(id, RequireIfMatch()), ct);
        return NoContent();
    }

    private long RequireIfMatch() => IfMatchHeader.Require(Request);

    private ProjectDto WithEtag(ProjectDto project)
    {
        IfMatchHeader.Write(Response, project.RowVersion);
        return project;
    }
}
