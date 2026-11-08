using FluentValidation;
using MediatR;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.User.Application.Abstractions;
using NexUS.User.Application.Common;
using NexUS.User.Domain.Tenants;

namespace NexUS.User.Application.Tenants;

public sealed record TenantDto(Guid Id, string Code, string Name, bool IsActive, long RowVersion);

public sealed record GetCurrentTenantQuery : IRequest<TenantDto>;

public sealed class GetCurrentTenantQueryHandler(ITenantRepository tenants) : IRequestHandler<GetCurrentTenantQuery, TenantDto>
{
    public async Task<TenantDto> Handle(GetCurrentTenantQuery request, CancellationToken ct) =>
        ToDto(await tenants.GetCurrentAsync(ct) ?? throw new NotFoundException("Tenant tidak ditemukan."));

    internal static TenantDto ToDto(Tenant tenant) =>
        new(tenant.PublicId, tenant.Code, tenant.Name, tenant.IsActive, tenant.RowVersion);
}

public sealed record UpdateCurrentTenantCommand(string Name, long ExpectedRowVersion) : IRequest<TenantDto>;

public sealed class UpdateCurrentTenantCommandValidator : AbstractValidator<UpdateCurrentTenantCommand>
{
    public UpdateCurrentTenantCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Tenant.NameMaxLength);
        RuleFor(x => x.ExpectedRowVersion).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateCurrentTenantCommandHandler(ITenantRepository tenants, IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateCurrentTenantCommand, TenantDto>
{
    public async Task<TenantDto> Handle(UpdateCurrentTenantCommand request, CancellationToken ct)
    {
        var tenant = await tenants.GetCurrentAsync(ct) ?? throw new NotFoundException("Tenant tidak ditemukan.");

        tenant.Rename(request.Name);
        unitOfWork.ExpectRowVersion(tenant, request.ExpectedRowVersion);
        await unitOfWork.SaveChangesAsync(ct);

        return GetCurrentTenantQueryHandler.ToDto(tenant);
    }
}
