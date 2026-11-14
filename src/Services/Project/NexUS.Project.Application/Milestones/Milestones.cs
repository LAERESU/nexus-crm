using FluentValidation;
using MediatR;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.BuildingBlocks.Common.Results;
using NexUS.Project.Application.Abstractions;
using NexUS.Project.Application.Common;
using NexUS.Project.Application.Projects;
using NexUS.Project.Domain.Milestones;

namespace NexUS.Project.Application.Milestones;

/// <param name="Status"><c>open</c> atau <c>completed</c>.</param>
/// <param name="RowVersion">Dikirim balik klien lewat header <c>If-Match</c> saat mengubah data.</param>
public sealed record MilestoneDto(
    Guid Id, Guid ProjectId, string Name, string? Description, DateOnly DueDate, string Status, long RowVersion, DateTimeOffset CreatedAt)
{
    public static MilestoneDto From(Milestone m, Guid projectId) =>
        new(m.PublicId, projectId, m.Name, m.Description, m.DueDate, MilestoneStatusNames.ToApi(m.Status), m.RowVersion, m.CreatedAt);
}

/// <summary>Nama status milestone di API (huruf kecil); kolom STATUS memakai huruf besar.</summary>
public static class MilestoneStatusNames
{
    public const string Allowed = "open, completed";

    public static bool TryParse(string? value, out MilestoneStatus status) => Milestone.TryParseStatus(value, out status);

    public static string ToApi(MilestoneStatus status) => Milestone.ToName(status).ToLowerInvariant();
}

/// <summary>Aturan field milestone yang sama untuk buat dan ubah.</summary>
internal static class MilestoneRules
{
    public static IRuleBuilderOptions<T, string> MilestoneName<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MaximumLength(Milestone.NameMaxLength);

    public static IRuleBuilderOptions<T, string?> MilestoneDescription<T>(this IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(Milestone.DescriptionMaxLength);

    public static IRuleBuilderOptions<T, DateOnly> MilestoneDueDate<T>(this IRuleBuilder<T, DateOnly> rule) =>
        rule.NotEmpty().WithMessage("Tanggal jatuh tempo wajib diisi.");
}

public sealed record ListMilestonesQuery(Guid ProjectId, int Page = 1, int Size = PagedResult<MilestoneDto>.DefaultSize)
    : IRequest<PagedResult<MilestoneDto>>;

public sealed class ListMilestonesQueryValidator : AbstractValidator<ListMilestonesQuery>
{
    public ListMilestonesQueryValidator()
    {
        RuleFor(x => x.Page).InclusiveBetween(1, 100_000);
        RuleFor(x => x.Size).InclusiveBetween(1, PagedResult<MilestoneDto>.MaxSize);
    }
}

public sealed class ListMilestonesQueryHandler(IProjectRepository projects, IMilestoneReadService milestones, ProjectGuard guard)
    : IRequestHandler<ListMilestonesQuery, PagedResult<MilestoneDto>>
{
    public async Task<PagedResult<MilestoneDto>> Handle(ListMilestonesQuery request, CancellationToken ct)
    {
        var projectId = await projects.FindIdAsync(request.ProjectId, ct) ?? throw new NotFoundException("Proyek tidak ditemukan.");
        await guard.EnsureCanReadAsync(request.ProjectId, ct);

        return await milestones.ListAsync(projectId, request.ProjectId, request.Page, request.Size, ct);
    }
}

public sealed record GetMilestoneQuery(Guid ProjectId, Guid MilestoneId) : IRequest<MilestoneDto>;

public sealed class GetMilestoneQueryHandler(IProjectRepository projects, IMilestoneReadService milestones, ProjectGuard guard)
    : IRequestHandler<GetMilestoneQuery, MilestoneDto>
{
    public async Task<MilestoneDto> Handle(GetMilestoneQuery request, CancellationToken ct)
    {
        var projectId = await projects.FindIdAsync(request.ProjectId, ct) ?? throw new NotFoundException("Proyek tidak ditemukan.");
        await guard.EnsureCanReadAsync(request.ProjectId, ct);

        return await milestones.GetAsync(projectId, request.ProjectId, request.MilestoneId, ct)
            ?? throw new NotFoundException("Milestone tidak ditemukan.");
    }
}

public sealed record CreateMilestoneCommand(Guid ProjectId, string Name, string? Description, DateOnly DueDate) : IRequest<MilestoneDto>;

public sealed class CreateMilestoneCommandValidator : AbstractValidator<CreateMilestoneCommand>
{
    public CreateMilestoneCommandValidator()
    {
        RuleFor(x => x.Name).MilestoneName();
        RuleFor(x => x.Description).MilestoneDescription();
        RuleFor(x => x.DueDate).MilestoneDueDate();
    }
}

public sealed class CreateMilestoneCommandHandler(
    IProjectRepository projects, IMilestoneRepository milestones, ProjectGuard guard, IUnitOfWork unitOfWork)
    : IRequestHandler<CreateMilestoneCommand, MilestoneDto>
{
    public async Task<MilestoneDto> Handle(CreateMilestoneCommand request, CancellationToken ct)
    {
        var project = await projects.FindAsync(request.ProjectId, ct) ?? throw new NotFoundException("Proyek tidak ditemukan.");
        await guard.EnsureCanManageAsync(ProjectPermissions.Update, project, ct);

        var milestone = Milestone.Create(project, request.Name, request.Description, request.DueDate);
        await milestones.AddAsync(milestone, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return MilestoneDto.From(milestone, project.PublicId);
    }
}

public sealed record UpdateMilestoneCommand(
    Guid ProjectId, Guid MilestoneId, string Name, string? Description, DateOnly DueDate, string Status, long ExpectedRowVersion)
    : IRequest<MilestoneDto>;

public sealed class UpdateMilestoneCommandValidator : AbstractValidator<UpdateMilestoneCommand>
{
    public UpdateMilestoneCommandValidator()
    {
        RuleFor(x => x.Name).MilestoneName();
        RuleFor(x => x.Description).MilestoneDescription();
        RuleFor(x => x.DueDate).MilestoneDueDate();
        RuleFor(x => x.Status).Must(s => MilestoneStatusNames.TryParse(s, out _))
            .WithMessage($"Status harus salah satu dari: {MilestoneStatusNames.Allowed}.");
        RuleFor(x => x.ExpectedRowVersion).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateMilestoneCommandHandler(
    IProjectRepository projects, IMilestoneRepository milestones, ProjectGuard guard, IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateMilestoneCommand, MilestoneDto>
{
    public async Task<MilestoneDto> Handle(UpdateMilestoneCommand request, CancellationToken ct)
    {
        var project = await projects.FindAsync(request.ProjectId, ct) ?? throw new NotFoundException("Proyek tidak ditemukan.");
        await guard.EnsureCanManageAsync(ProjectPermissions.Update, project, ct);
        var milestone = await milestones.FindAsync(project.Id, request.MilestoneId, ct)
            ?? throw new NotFoundException("Milestone tidak ditemukan.");

        MilestoneStatusNames.TryParse(request.Status, out var status); // sudah divalidasi
        unitOfWork.ExpectRowVersion(milestone, request.ExpectedRowVersion);
        milestone.Update(project, request.Name, request.Description, request.DueDate, status);
        await unitOfWork.SaveChangesAsync(ct);

        return MilestoneDto.From(milestone, project.PublicId);
    }
}
