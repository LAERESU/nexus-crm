using FluentValidation;
using MediatR;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.BuildingBlocks.Common.Results;
using NexUS.User.Application.Abstractions;

namespace NexUS.User.Application.Departments;

public sealed record ListDepartmentsQuery(string? Search, int Page = 1, int Size = PagedResult<DepartmentDto>.DefaultSize)
    : IRequest<PagedResult<DepartmentDto>>;

public sealed class ListDepartmentsQueryValidator : AbstractValidator<ListDepartmentsQuery>
{
    public ListDepartmentsQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.Page).InclusiveBetween(1, 100_000); // batas atas mencegah overflow offset
        RuleFor(x => x.Size).InclusiveBetween(1, PagedResult<DepartmentDto>.MaxSize);
    }
}

public sealed class ListDepartmentsQueryHandler(IDepartmentReadService departments)
    : IRequestHandler<ListDepartmentsQuery, PagedResult<DepartmentDto>>
{
    public Task<PagedResult<DepartmentDto>> Handle(ListDepartmentsQuery request, CancellationToken ct) =>
        departments.ListAsync(request.Search?.Trim(), request.Page, request.Size, ct);
}

public sealed record GetDepartmentQuery(Guid Id) : IRequest<DepartmentDto>;

public sealed class GetDepartmentQueryHandler(IDepartmentReadService departments) : IRequestHandler<GetDepartmentQuery, DepartmentDto>
{
    public async Task<DepartmentDto> Handle(GetDepartmentQuery request, CancellationToken ct) =>
        await departments.GetAsync(request.Id, ct) ?? throw new NotFoundException("Departemen tidak ditemukan.");
}
