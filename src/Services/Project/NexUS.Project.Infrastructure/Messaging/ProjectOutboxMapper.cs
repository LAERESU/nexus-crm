using NexUS.BuildingBlocks.Messaging;
using NexUS.BuildingBlocks.Messaging.Contracts;
using NexUS.Project.Domain.Projects;

namespace NexUS.Project.Infrastructure.Messaging;

/// <summary>
/// Kontrak event Project Service yang dipublikasikan (NX-028). Event milestone belum punya kontrak (belum ada
/// consumer-nya) dan tetap tertunda di outbox.
/// </summary>
public sealed class ProjectOutboxMapper : OutboxEventMapper
{
    public ProjectOutboxMapper()
    {
        Map<ProjectCreatedDomainEvent>((e, id, at) => new ProjectCreated(id, at, e.TenantId, e.ProjectId, e.Code, e.Name, e.OwnerId));
        Map<ProjectUpdatedDomainEvent>((e, id, at) =>
            new ProjectUpdated(id, at, e.TenantId, e.ProjectId, e.Code, e.Name, e.Status.ToLowerInvariant()));
        Map<ProjectArchivedDomainEvent>((e, id, at) => new ProjectArchived(id, at, e.TenantId, e.ProjectId));
        Map<ProjectMemberAddedDomainEvent>((e, id, at) =>
            new MemberAdded(id, at, e.TenantId, e.ProjectId, e.UserId, e.Role.ToLowerInvariant()));
        Map<ProjectMemberRoleChangedDomainEvent>((e, id, at) =>
            new MemberRoleChanged(id, at, e.TenantId, e.ProjectId, e.UserId, e.Role.ToLowerInvariant()));
        Map<ProjectMemberRemovedDomainEvent>((e, id, at) => new MemberRemoved(id, at, e.TenantId, e.ProjectId, e.UserId));
    }
}
