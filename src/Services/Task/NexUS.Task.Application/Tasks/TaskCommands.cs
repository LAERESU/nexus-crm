using FluentValidation;
using MediatR;
using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.Tasks.Application.Abstractions;
using NexUS.Tasks.Application.Common;
using NexUS.Tasks.Domain.Tasks;

namespace NexUS.Tasks.Application.Tasks;

/// <param name="ParentTaskId">Tugas induk bila membuat subtask.</param>
/// <param name="Priority">Kosong berarti <c>medium</c>.</param>
public sealed record CreateTaskCommand(
    Guid ProjectId, Guid? ParentTaskId, string Title, string? Description, string? Priority, DateOnly? DueDate) : IRequest<TaskDto>;

public sealed class CreateTaskCommandValidator : AbstractValidator<CreateTaskCommand>
{
    public CreateTaskCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(TaskItem.TitleMaxLength);
        RuleFor(x => x.Description).MaximumLength(TaskItem.DescriptionMaxLength);
        RuleFor(x => x.Priority)
            .Must(p => string.IsNullOrWhiteSpace(p) || TaskNames.TryParsePriority(p, out _))
            .WithMessage($"Prioritas harus salah satu dari: {TaskNames.AllowedPriorities}.");
    }
}

public sealed class CreateTaskCommandHandler(
    ITaskRepository tasks, TaskGuard guard, IUnitOfWork unitOfWork, ICurrentUser currentUser)
    : IRequestHandler<CreateTaskCommand, TaskDto>
{
    public async Task<TaskDto> Handle(CreateTaskCommand request, CancellationToken ct)
    {
        await guard.EnsureCanWriteAsync(request.ProjectId, TaskPermissions.Create, ct);

        TaskItem? parent = null;
        if (request.ParentTaskId is { } parentId)
        {
            parent = await tasks.FindAsync(parentId, ct);
            // Induk di proyek lain dijawab sama dengan induk yang tidak ada: keberadaan tugas di proyek yang mungkin tidak
            // diikuti user tidak boleh bisa ditebak dari kode error.
            if (parent is null || parent.ProjectId != request.ProjectId) throw new NotFoundException("Tugas induk tidak ditemukan.");
        }

        var priority = TaskNames.TryParsePriority(request.Priority, out var parsed) ? parsed : TaskPriority.Medium;
        var task = TaskItem.Create(
            currentUser.TenantId, request.ProjectId, parent, request.Title, request.Description, priority, request.DueDate,
            currentUser.UserId);
        await tasks.AddAsync(task, ct);
        await unitOfWork.SaveChangesAsync(ct); // juga menyimpan event TaskCreated ke outbox

        // Tugas baru belum punya assignee dan induknya sudah dimuat: DTO dibentuk tanpa membaca ulang.
        return TaskDto.From(task, parent?.PublicId, []);
    }
}

public sealed record UpdateTaskCommand(
    Guid Id, string Title, string? Description, string Priority, DateOnly? DueDate, long ExpectedRowVersion) : IRequest<TaskDto>;

public sealed class UpdateTaskCommandValidator : AbstractValidator<UpdateTaskCommand>
{
    public UpdateTaskCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(TaskItem.TitleMaxLength);
        RuleFor(x => x.Description).MaximumLength(TaskItem.DescriptionMaxLength);
        RuleFor(x => x.Priority).Must(p => TaskNames.TryParsePriority(p, out _))
            .WithMessage($"Prioritas harus salah satu dari: {TaskNames.AllowedPriorities}.");
        RuleFor(x => x.ExpectedRowVersion).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateTaskCommandHandler(ITaskRepository tasks, ITaskReadService readService, TaskGuard guard, IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateTaskCommand, TaskDto>
{
    public async Task<TaskDto> Handle(UpdateTaskCommand request, CancellationToken ct)
    {
        var task = await tasks.FindAsync(request.Id, ct) ?? throw new NotFoundException("Tugas tidak ditemukan.");
        await guard.EnsureCanWriteAsync(task.ProjectId, TaskPermissions.Update, ct);

        TaskNames.TryParsePriority(request.Priority, out var priority); // sudah divalidasi
        unitOfWork.ExpectRowVersion(task, request.ExpectedRowVersion);
        task.Update(request.Title, request.Description, priority, request.DueDate);
        await unitOfWork.SaveChangesAsync(ct);

        return await readService.GetAsync(task.PublicId, ct) ?? throw new InvalidOperationException("Tugas yang baru diubah tidak terbaca.");
    }
}

public sealed record DeleteTaskCommand(Guid Id, long ExpectedRowVersion) : IRequest;

/// <summary>Soft delete; subtask aktif ikut dihapus.</summary>
public sealed class DeleteTaskCommandHandler(ITaskRepository tasks, TaskGuard guard, IUnitOfWork unitOfWork) : IRequestHandler<DeleteTaskCommand>
{
    public async Task Handle(DeleteTaskCommand request, CancellationToken ct)
    {
        var task = await tasks.FindAsync(request.Id, ct) ?? throw new NotFoundException("Tugas tidak ditemukan.");
        await guard.EnsureCanWriteAsync(task.ProjectId, TaskPermissions.Delete, ct);

        unitOfWork.ExpectRowVersion(task, request.ExpectedRowVersion);
        foreach (var subtask in await tasks.FindSubtasksAsync(task.Id, ct)) subtask.Delete();
        task.Delete();
        await unitOfWork.SaveChangesAsync(ct);
    }
}

/// <param name="ExpectedRowVersion">Dari header <c>If-Match</c> bila dikirim; tanpa header, perubahan bersamaan tetap
/// tertangkap lewat ROW_VERSION yang dimuat (409).</param>
public sealed record ChangeTaskStatusCommand(Guid Id, string Status, long? ExpectedRowVersion) : IRequest<TaskDto>;

public sealed class ChangeTaskStatusCommandValidator : AbstractValidator<ChangeTaskStatusCommand>
{
    public ChangeTaskStatusCommandValidator()
    {
        RuleFor(x => x.Status).Must(s => TaskNames.TryParseStatus(s, out _))
            .WithMessage($"Status harus salah satu dari: {TaskNames.AllowedStatuses}.");
        RuleFor(x => x.ExpectedRowVersion).GreaterThanOrEqualTo(0);
    }
}

/// <summary>Memindah status tugas (state machine di Domain) dan mencatat riwayatnya dalam transaksi yang sama.</summary>
public sealed class ChangeTaskStatusCommandHandler(
    ITaskRepository tasks, ITaskReadService readService, TaskGuard guard, IUnitOfWork unitOfWork, ICurrentUser currentUser,
    TimeProvider timeProvider)
    : IRequestHandler<ChangeTaskStatusCommand, TaskDto>
{
    public async Task<TaskDto> Handle(ChangeTaskStatusCommand request, CancellationToken ct)
    {
        var task = await tasks.FindAsync(request.Id, ct) ?? throw new NotFoundException("Tugas tidak ditemukan.");
        await guard.EnsureCanWriteAsync(task.ProjectId, TaskPermissions.Update, ct);

        if (request.ExpectedRowVersion is { } expected) unitOfWork.ExpectRowVersion(task, expected);
        TaskNames.TryParseStatus(request.Status, out var status); // sudah divalidasi
        if (task.ChangeStatus(status, currentUser.UserId, timeProvider.GetUtcNow()))
            await unitOfWork.SaveChangesAsync(ct); // riwayat + event TaskStatusChanged ke outbox

        return await readService.GetAsync(task.PublicId, ct) ?? throw new InvalidOperationException("Tugas yang baru diubah tidak terbaca.");
    }
}
