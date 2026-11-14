using FluentValidation;
using MediatR;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.BuildingBlocks.Common.Results;
using NexUS.Project.Application.Abstractions;
using NexUS.Project.Application.Common;
using NexUS.Project.Domain.Projects;

namespace NexUS.Project.Application.Projects;

/// <param name="Role"><c>manager</c>, <c>member</c>, atau <c>viewer</c>.</param>
/// <param name="FullName">Dari read model user; null bila user belum dikenal service ini.</param>
/// <param name="IsActive">Status user di User Service; null bila belum dikenal.</param>
public sealed record ProjectMemberDto(Guid UserId, string? FullName, string? Email, string Role, DateTimeOffset JoinedAt, bool? IsActive);

/// <summary>Nama peran proyek di API (huruf kecil); kolom PROJECT_ROLE memakai huruf besar.</summary>
public static class ProjectRoleNames
{
    public const string Allowed = "manager, member, viewer";

    public static bool TryParse(string? value, out ProjectRole role) => ProjectMember.TryParseRole(value, out role);

    public static string ToApi(ProjectRole role) => ProjectMember.ToName(role).ToLowerInvariant();
}

public sealed record ListProjectMembersQuery(Guid ProjectId, int Page = 1, int Size = PagedResult<ProjectMemberDto>.DefaultSize)
    : IRequest<PagedResult<ProjectMemberDto>>;

public sealed class ListProjectMembersQueryValidator : AbstractValidator<ListProjectMembersQuery>
{
    public ListProjectMembersQueryValidator()
    {
        RuleFor(x => x.Page).InclusiveBetween(1, 100_000);
        RuleFor(x => x.Size).InclusiveBetween(1, PagedResult<ProjectMemberDto>.MaxSize);
    }
}

public sealed class ListProjectMembersQueryHandler(IProjectRepository projects, IProjectReadService readService, ProjectGuard guard)
    : IRequestHandler<ListProjectMembersQuery, PagedResult<ProjectMemberDto>>
{
    public async Task<PagedResult<ProjectMemberDto>> Handle(ListProjectMembersQuery request, CancellationToken ct)
    {
        if (await projects.FindAsync(request.ProjectId, ct) is null) throw new NotFoundException("Proyek tidak ditemukan.");
        await guard.EnsureCanReadAsync(request.ProjectId, ct);

        return await readService.ListMembersAsync(request.ProjectId, request.Page, request.Size, ct);
    }
}

public sealed record AddProjectMemberCommand(Guid ProjectId, Guid UserId, string Role) : IRequest<ProjectMemberDto>;

public sealed class AddProjectMemberCommandValidator : AbstractValidator<AddProjectMemberCommand>
{
    public AddProjectMemberCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Role).Must(r => ProjectRoleNames.TryParse(r, out _))
            .WithMessage($"Peran harus salah satu dari: {ProjectRoleNames.Allowed}.");
    }
}

public sealed class AddProjectMemberCommandHandler(
    IProjectRepository projects, IProjectReadService readService, IUserDirectory users, ProjectGuard guard,
    IUnitOfWork unitOfWork, TimeProvider timeProvider) : IRequestHandler<AddProjectMemberCommand, ProjectMemberDto>
{
    public async Task<ProjectMemberDto> Handle(AddProjectMemberCommand request, CancellationToken ct)
    {
        var project = await projects.FindWithMembersAsync(request.ProjectId, ct) ?? throw new NotFoundException("Proyek tidak ditemukan.");
        await guard.EnsureCanManageAsync(ProjectPermissions.MemberManage, project, ct);

        // User yang belum dikenal read model (event-nya belum tiba) tetap diterima; yang diketahui nonaktif ditolak.
        if (await users.IsActiveAsync(request.UserId, ct) == false)
            throw new DomainException("User nonaktif tidak bisa dijadikan anggota proyek.");

        ProjectRoleNames.TryParse(request.Role, out var role); // sudah divalidasi
        project.AddMember(request.UserId, role, timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(ct);

        return await MemberReader.GetAsync(readService, request.ProjectId, request.UserId, ct);
    }
}

public sealed record ChangeProjectMemberRoleCommand(Guid ProjectId, Guid UserId, string Role) : IRequest<ProjectMemberDto>;

public sealed class ChangeProjectMemberRoleCommandValidator : AbstractValidator<ChangeProjectMemberRoleCommand>
{
    public ChangeProjectMemberRoleCommandValidator() =>
        RuleFor(x => x.Role).Must(r => ProjectRoleNames.TryParse(r, out _))
            .WithMessage($"Peran harus salah satu dari: {ProjectRoleNames.Allowed}.");
}

public sealed class ChangeProjectMemberRoleCommandHandler(
    IProjectRepository projects, IProjectReadService readService, ProjectGuard guard, IUnitOfWork unitOfWork)
    : IRequestHandler<ChangeProjectMemberRoleCommand, ProjectMemberDto>
{
    public async Task<ProjectMemberDto> Handle(ChangeProjectMemberRoleCommand request, CancellationToken ct)
    {
        var project = await projects.FindWithMembersAsync(request.ProjectId, ct) ?? throw new NotFoundException("Proyek tidak ditemukan.");
        await guard.EnsureCanManageAsync(ProjectPermissions.MemberManage, project, ct);

        ProjectRoleNames.TryParse(request.Role, out var role); // sudah divalidasi
        if (project.ChangeMemberRole(request.UserId, role) is null) throw new NotFoundException("User bukan anggota proyek ini.");
        await unitOfWork.SaveChangesAsync(ct);

        return await MemberReader.GetAsync(readService, request.ProjectId, request.UserId, ct);
    }
}

public sealed record RemoveProjectMemberCommand(Guid ProjectId, Guid UserId) : IRequest;

public sealed class RemoveProjectMemberCommandHandler(IProjectRepository projects, ProjectGuard guard, IUnitOfWork unitOfWork)
    : IRequestHandler<RemoveProjectMemberCommand>
{
    public async Task Handle(RemoveProjectMemberCommand request, CancellationToken ct)
    {
        var project = await projects.FindWithMembersAsync(request.ProjectId, ct) ?? throw new NotFoundException("Proyek tidak ditemukan.");
        await guard.EnsureCanManageAsync(ProjectPermissions.MemberManage, project, ct);

        if (!project.RemoveMember(request.UserId)) throw new NotFoundException("User bukan anggota proyek ini.");
        await unitOfWork.SaveChangesAsync(ct);
    }
}

internal static class MemberReader
{
    /// <summary>Anggota dibaca ulang karena nama dan status aktifnya berasal dari read model user.</summary>
    public static async Task<ProjectMemberDto> GetAsync(IProjectReadService readService, Guid projectId, Guid userId, CancellationToken ct) =>
        await readService.GetMemberAsync(projectId, userId, ct)
        ?? throw new InvalidOperationException("Anggota yang baru disimpan tidak terbaca.");
}
