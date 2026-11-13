using FluentValidation;
using MediatR;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.Project.Application.Abstractions;
using NexUS.Project.Application.Common;
using NexUS.Project.Domain.Projects;
using ProjectEntity = NexUS.Project.Domain.Projects.Project;

namespace NexUS.Project.Application.Projects;

public sealed record UpdateProjectCommand(
    Guid Id, string Name, string? Description, string Status, DateOnly? StartDate, DateOnly? EndDate, Guid? DepartmentId,
    long ExpectedRowVersion) : IRequest<ProjectDto>;

public sealed class UpdateProjectCommandValidator : AbstractValidator<UpdateProjectCommand>
{
    public UpdateProjectCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(ProjectEntity.NameMaxLength);
        RuleFor(x => x.Description).MaximumLength(ProjectEntity.DescriptionMaxLength);
        RuleFor(x => x.Status)
            .Must(s => ProjectStatusNames.TryParse(s, out var status) && status != ProjectStatus.Archived)
            .WithMessage("Status harus planning, active, on_hold, atau completed; arsip memakai DELETE.");
        RuleFor(x => x.EndDate)
            .Must((command, end) => command.StartDate is null || end is null || command.StartDate <= end)
            .WithMessage("Tanggal selesai tidak boleh sebelum tanggal mulai.");
        RuleFor(x => x.ExpectedRowVersion).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateProjectCommandHandler(IProjectRepository projects, IProjectReadService readService, IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateProjectCommand, ProjectDto>
{
    public async Task<ProjectDto> Handle(UpdateProjectCommand request, CancellationToken ct)
    {
        var project = await projects.FindAsync(request.Id, ct) ?? throw new NotFoundException("Proyek tidak ditemukan.");
        ProjectStatusNames.TryParse(request.Status, out var status); // sudah divalidasi

        // Versi diperiksa lebih dulu: klien dengan data basi mendapat 409, bukan pesan aturan bisnis atas data lama.
        unitOfWork.ExpectRowVersion(project, request.ExpectedRowVersion);
        project.Update(request.Name, request.Description, status, request.StartDate, request.EndDate, request.DepartmentId);
        await unitOfWork.SaveChangesAsync(ct);

        return await readService.GetAsync(project.PublicId, ct)
            ?? throw new InvalidOperationException("Proyek yang baru diubah tidak terbaca.");
    }
}

public sealed record ArchiveProjectCommand(Guid Id, long ExpectedRowVersion) : IRequest;

public sealed class ArchiveProjectCommandHandler(IProjectRepository projects, IUnitOfWork unitOfWork) : IRequestHandler<ArchiveProjectCommand>
{
    public async Task Handle(ArchiveProjectCommand request, CancellationToken ct)
    {
        var project = await projects.FindAsync(request.Id, ct) ?? throw new NotFoundException("Proyek tidak ditemukan.");

        unitOfWork.ExpectRowVersion(project, request.ExpectedRowVersion);
        project.Archive();
        await unitOfWork.SaveChangesAsync(ct);
    }
}
