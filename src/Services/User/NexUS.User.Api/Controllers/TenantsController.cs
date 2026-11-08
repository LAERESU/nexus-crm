using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexUS.User.Application.Tenants;

namespace NexUS.User.Api.Controllers;

public sealed record UpdateTenantRequest(string Name);

[ApiController]
[Route("api/v1/tenants")]
public sealed class TenantsController(ISender mediator) : ControllerBase
{
    /// <summary>Tenant milik user yang sedang login.</summary>
    [HttpGet("current")]
    [Authorize(Policy = Policies.Authenticated)]
    [ProducesResponseType<TenantDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<TenantDto> GetCurrent(CancellationToken ct) => WithEtag(await mediator.Send(new GetCurrentTenantQuery(), ct));

    /// <summary>Mengubah nama tenant sendiri. Wajib mengirim header <c>If-Match</c>.</summary>
    [HttpPut("current")]
    [Authorize(Policy = Policies.UserManage)]
    [ProducesResponseType<TenantDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<TenantDto> UpdateCurrent(UpdateTenantRequest request, CancellationToken ct) =>
        WithEtag(await mediator.Send(new UpdateCurrentTenantCommand(request.Name, IfMatchHeader.Require(Request)), ct));

    private TenantDto WithEtag(TenantDto tenant)
    {
        IfMatchHeader.Write(Response, tenant.RowVersion);
        return tenant;
    }
}
