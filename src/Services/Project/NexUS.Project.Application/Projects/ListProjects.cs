using FluentValidation;
using MediatR;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.BuildingBlocks.Common.Results;
using NexUS.Project.Application.Abstractions;

namespace NexUS.Project.Application.Projects;

/// <param name="Status">Tanpa status, proyek yang diarsipkan tidak ikut; <c>archived</c> menampilkan arsip.</param>
public sealed record ListProjectsQuery(
    string? Search, string? Status, Guid? OwnerId, Guid? DepartmentId, int Page = 1, int Size = PagedResult<ProjectDto>.DefaultSize)
    : IRequest<PagedResult<ProjectDto>>;

public sealed class ListProjectsQueryValidator : AbstractValidator<ListProjectsQuery>
{
    public ListProjectsQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.Page).InclusiveBetween(1, 100_000); // batas atas mencegah overflow offset
        RuleFor(x => x.Size).InclusiveBetween(1, PagedResult<ProjectDto>.MaxSize);
        RuleFor(x => x.Status)
            .Must(s => s is null || ProjectStatusNames.TryParse(s, out _))
            .WithMessage($"Status harus salah satu dari: {ProjectStatusNames.Allowed}.");
    }
}

public sealed class ListProjectsQueryHandler(IProjectReadService projects, ProjectGuard guard)
    : IRequestHandler<ListProjectsQuery, PagedResult<ProjectDto>>
{
    public async Task<PagedResult<ProjectDto>> Handle(ListProjectsQuery request, CancellationToken ct) =>
        // Tanpa izin melihat semua proyek, daftar hanya berisi proyek yang diikuti user.
        await projects.ListAsync(
            new ProjectFilter(
                request.Search?.Trim(), request.Status, request.OwnerId, request.DepartmentId, await guard.ListScopeAsync(ct),
                request.Page, request.Size),
            ct);
}

public sealed record GetProjectQuery(Guid Id) : IRequest<ProjectDto>;

public sealed class GetProjectQueryHandler(IProjectReadService projects, ProjectGuard guard) : IRequestHandler<GetProjectQuery, ProjectDto>
{
    public async Task<ProjectDto> Handle(GetProjectQuery request, CancellationToken ct)
    {
        var project = await projects.GetAsync(request.Id, ct) ?? throw new NotFoundException("Proyek tidak ditemukan.");
        await guard.EnsureCanReadAsync(request.Id, ct);
        return project;
    }
}
