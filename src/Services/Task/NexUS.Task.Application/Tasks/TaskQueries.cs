using FluentValidation;
using MediatR;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.BuildingBlocks.Common.Results;
using NexUS.Tasks.Application.Abstractions;

namespace NexUS.Tasks.Application.Tasks;

/// <param name="ProjectId">Tanpa proyek: tugas di semua proyek yang diikuti user (semua proyek bagi admin tenant/auditor).</param>
/// <param name="DueBefore">Jatuh tempo pada atau sebelum tanggal ini.</param>
public sealed record ListTasksQuery(
    Guid? ProjectId, Guid? AssigneeId, string? Status, DateOnly? DueBefore, int Page = 1, int Size = PagedResult<TaskDto>.DefaultSize)
    : IRequest<PagedResult<TaskDto>>;

public sealed class ListTasksQueryValidator : AbstractValidator<ListTasksQuery>
{
    public ListTasksQueryValidator()
    {
        RuleFor(x => x.Page).InclusiveBetween(1, 100_000);
        RuleFor(x => x.Size).InclusiveBetween(1, PagedResult<TaskDto>.MaxSize);
        RuleFor(x => x.Status).Must(s => s is null || TaskNames.TryParseStatus(s, out _))
            .WithMessage($"Status harus salah satu dari: {TaskNames.AllowedStatuses}.");
    }
}

public sealed class ListTasksQueryHandler(ITaskReadService tasks, TaskGuard guard) : IRequestHandler<ListTasksQuery, PagedResult<TaskDto>>
{
    public async Task<PagedResult<TaskDto>> Handle(ListTasksQuery request, CancellationToken ct)
    {
        Guid? memberScope = null;
        if (request.ProjectId is { } projectId) await guard.EnsureCanReadAsync(projectId, ct);
        else memberScope = await guard.ListScopeAsync(ct);

        var status = TaskNames.TryParseStatus(request.Status, out var parsed) ? parsed : (Domain.Tasks.TaskItemStatus?)null;
        return await tasks.ListAsync(
            new TaskFilter(request.ProjectId, request.AssigneeId, status, request.DueBefore, memberScope, request.Page, request.Size), ct);
    }
}

public sealed record GetTaskQuery(Guid Id) : IRequest<TaskDto>;

public sealed class GetTaskQueryHandler(ITaskReadService tasks, TaskGuard guard) : IRequestHandler<GetTaskQuery, TaskDto>
{
    public async Task<TaskDto> Handle(GetTaskQuery request, CancellationToken ct)
    {
        var task = await tasks.GetAsync(request.Id, ct) ?? throw new NotFoundException("Tugas tidak ditemukan.");
        await guard.EnsureCanReadAsync(task.ProjectId, ct);
        return task;
    }
}
