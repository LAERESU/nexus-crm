using System.Globalization;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexUS.BuildingBlocks.Common.Results;
using NexUS.Tasks.Application.Tasks;

namespace NexUS.Tasks.Api.Controllers;

/// <param name="ParentTaskId">Tugas induk bila membuat subtask (maksimum satu tingkat).</param>
/// <param name="Priority"><c>low</c>, <c>medium</c> (bawaan), <c>high</c>, atau <c>urgent</c>.</param>
public sealed record CreateTaskRequest(
    Guid ProjectId, Guid? ParentTaskId, string Title, string? Description, string? Priority, DateOnly? DueDate);

/// <param name="Priority"><c>low</c>, <c>medium</c>, <c>high</c>, atau <c>urgent</c>.</param>
public sealed record UpdateTaskRequest(string Title, string? Description, string Priority, DateOnly? DueDate);

/// <summary>
/// Tugas proyek. Hanya anggota proyek yang bisa melihat tugasnya, dan hanya anggota bukan pengamat yang bisa
/// membuat/mengubah/menghapus; tugas proyek yang diarsipkan hanya bisa dilihat.
/// </summary>
[ApiController]
[Route("api/v1/tasks")]
public sealed class TasksController(ISender mediator) : ControllerBase
{
    /// <summary>
    /// Daftar tugas dengan filter proyek, assignee, status, dan jatuh tempo (pada atau sebelum <c>dueBefore</c>), dengan
    /// paging. Tanpa <c>projectId</c>: tugas di semua proyek yang diikuti user. <c>due_before</c> sama dengan <c>dueBefore</c>.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = Policies.TaskRead)]
    [ProducesResponseType<PagedResult<TaskDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<TaskDto>> List(
        [FromQuery] Guid? projectId, [FromQuery] Guid? assigneeId, [FromQuery] string? status, [FromQuery] DateOnly? dueBefore,
        [FromQuery(Name = "due_before")] DateOnly? dueBeforeAlias,
        [FromQuery] int page = 1, [FromQuery] int size = PagedResult<TaskDto>.DefaultSize, CancellationToken ct = default) =>
        // due_before (nama di story/ARCHITECTURE) dan dueBefore (camelCase) sama-sama diterima.
        mediator.Send(new ListTasksQuery(projectId, assigneeId, status, dueBefore ?? dueBeforeAlias, page, size), ct);

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.TaskRead)]
    [ProducesResponseType<TaskDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<TaskDto> Get(Guid id, CancellationToken ct) => WithEtag(await mediator.Send(new GetTaskQuery(id), ct));

    /// <summary>Membuat tugas berstatus <c>todo</c>; dengan <c>parentTaskId</c> membuat subtask.</summary>
    [HttpPost]
    [Authorize(Policy = Policies.TaskCreate)]
    [ProducesResponseType<TaskDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<CreatedAtActionResult> Create(CreateTaskRequest request, CancellationToken ct)
    {
        var task = await mediator.Send(new CreateTaskCommand(
            request.ProjectId, request.ParentTaskId, request.Title, request.Description, request.Priority, request.DueDate), ct);
        WithEtag(task);
        return CreatedAtAction(nameof(Get), new { id = task.Id }, task);
    }

    /// <summary>Mengubah judul, deskripsi, prioritas, dan jatuh tempo. Wajib header <c>If-Match</c>.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.TaskUpdate)]
    [ProducesResponseType<TaskDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<TaskDto> Update(Guid id, UpdateTaskRequest request, CancellationToken ct) =>
        WithEtag(await mediator.Send(new UpdateTaskCommand(
            id, request.Title, request.Description, request.Priority, request.DueDate, RequireIfMatch()), ct));

    /// <summary>Menghapus (soft delete) tugas beserta subtask-nya. Wajib header <c>If-Match</c>.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.TaskDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await mediator.Send(new DeleteTaskCommand(id, RequireIfMatch()), ct);
        return NoContent();
    }

    /// <summary>Header <c>If-Match</c> berisi <c>ROW_VERSION</c> (CONTEXT.md bagian 6); ETag di response memakai format yang sama.</summary>
    private long RequireIfMatch()
    {
        var raw = Request.Headers.IfMatch.ToString().Trim().Trim('"');
        if (long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var version)) return version;

        throw new ValidationException([new ValidationFailure("If-Match", "Header If-Match berisi versi data (rowVersion) wajib diisi.")]);
    }

    private TaskDto WithEtag(TaskDto task)
    {
        Response.Headers.ETag = $"\"{task.RowVersion.ToString(CultureInfo.InvariantCulture)}\"";
        return task;
    }
}
