using FluentValidation;
using MediatR;
using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.Project.Application.Abstractions;
using NexUS.Project.Application.Common;
using ProjectEntity = NexUS.Project.Domain.Projects.Project;

namespace NexUS.Project.Application.Projects;

public sealed record CreateProjectCommand(
    string Code, string Name, string? Description, DateOnly? StartDate, DateOnly? EndDate, Guid? DepartmentId) : IRequest<ProjectDto>;

public sealed class CreateProjectCommandValidator : AbstractValidator<CreateProjectCommand>
{
    public CreateProjectCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(ProjectEntity.CodeMaxLength)
            .Matches("^[A-Za-z][A-Za-z0-9_-]*$")
            .WithMessage("Kode proyek diawali huruf dan hanya berisi huruf, angka, garis bawah, atau tanda hubung.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(ProjectEntity.NameMaxLength);
        RuleFor(x => x.Description).MaximumLength(ProjectEntity.DescriptionMaxLength);
        RuleFor(x => x.EndDate)
            .Must((command, end) => command.StartDate is null || end is null || command.StartDate <= end)
            .WithMessage("Tanggal selesai tidak boleh sebelum tanggal mulai.");
    }
}

public sealed class CreateProjectCommandHandler(
    IProjectRepository projects, IProjectReadService readService, IUnitOfWork unitOfWork, ICurrentUser currentUser)
    : IRequestHandler<CreateProjectCommand, ProjectDto>
{
    public async Task<ProjectDto> Handle(CreateProjectCommand request, CancellationToken ct)
    {
        var code = ProjectEntity.NormalizeCode(request.Code);
        if (await projects.CodeExistsAsync(code, ct))
            throw new DomainException("Kode proyek sudah dipakai di tenant ini.");

        // Pembuat menjadi pemilik dan anggota berperan MANAGER (aturan di Domain).
        var project = ProjectEntity.Create(
            currentUser.TenantId, code, request.Name, request.Description, request.StartDate, request.EndDate,
            currentUser.UserId, request.DepartmentId);
        await projects.AddAsync(project, ct);
        await unitOfWork.SaveChangesAsync(ct); // juga menyimpan event ProjectCreated ke outbox

        return await readService.GetAsync(project.PublicId, ct)
            ?? throw new InvalidOperationException("Proyek yang baru dibuat tidak terbaca.");
    }
}
