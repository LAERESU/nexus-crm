using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexUS.BuildingBlocks.Common.Results;
using NexUS.User.Application.Departments;

namespace NexUS.User.Api.Controllers;

public sealed record CreateDepartmentRequest(string Code, string Name, Guid? ParentId);

public sealed record UpdateDepartmentRequest(string Name, Guid? ParentId);

[ApiController]
[Route("api/v1/departments")]
public sealed class DepartmentsController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Policies.UserRead)]
    [ProducesResponseType<PagedResult<DepartmentDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<DepartmentDto>> List(
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int size = PagedResult<DepartmentDto>.DefaultSize,
        CancellationToken ct = default) =>
        mediator.Send(new ListDepartmentsQuery(search, page, size), ct);

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.UserRead)]
    [ProducesResponseType<DepartmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<DepartmentDto> Get(Guid id, CancellationToken ct) => WithEtag(await mediator.Send(new GetDepartmentQuery(id), ct));

    /// <summary>Membuat departemen. Kode unik per tenant; <c>parentId</c> membentuk hierarki.</summary>
    [HttpPost]
    [Authorize(Policy = Policies.UserManage)]
    [ProducesResponseType<DepartmentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<CreatedAtActionResult> Create(CreateDepartmentRequest request, CancellationToken ct)
    {
        var department = await mediator.Send(new CreateDepartmentCommand(request.Code, request.Name, request.ParentId), ct);
        IfMatchHeader.Write(Response, department.RowVersion);
        return CreatedAtAction(nameof(Get), new { id = department.Id }, department);
    }

    /// <summary>Mengubah nama atau induk departemen. Wajib mengirim header <c>If-Match</c>.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.UserManage)]
    [ProducesResponseType<DepartmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<DepartmentDto> Update(Guid id, UpdateDepartmentRequest request, CancellationToken ct) =>
        WithEtag(await mediator.Send(new UpdateDepartmentCommand(id, request.Name, request.ParentId, IfMatchHeader.Require(Request)), ct));

    /// <summary>Menghapus (soft delete) departemen yang tidak punya sub-departemen maupun user.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.UserManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await mediator.Send(new DeleteDepartmentCommand(id, IfMatchHeader.Require(Request)), ct);
        return NoContent();
    }

    private DepartmentDto WithEtag(DepartmentDto department)
    {
        IfMatchHeader.Write(Response, department.RowVersion);
        return department;
    }
}
