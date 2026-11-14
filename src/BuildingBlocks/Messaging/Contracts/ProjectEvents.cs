namespace NexUS.BuildingBlocks.Messaging.Contracts;

// Event Project Service (NX-028). Status dan peran memakai nama API huruf kecil (mis. on_hold, manager).

public sealed record ProjectCreated(
    Guid EventId, DateTimeOffset OccurredAt, long TenantId, Guid ProjectId, string Code, string Name, Guid OwnerId) : IIntegrationEvent;

public sealed record ProjectUpdated(
    Guid EventId, DateTimeOffset OccurredAt, long TenantId, Guid ProjectId, string Code, string Name, string Status) : IIntegrationEvent;

public sealed record ProjectArchived(Guid EventId, DateTimeOffset OccurredAt, long TenantId, Guid ProjectId) : IIntegrationEvent;

public sealed record MemberAdded(
    Guid EventId, DateTimeOffset OccurredAt, long TenantId, Guid ProjectId, Guid UserId, string Role) : IIntegrationEvent;

public sealed record MemberRoleChanged(
    Guid EventId, DateTimeOffset OccurredAt, long TenantId, Guid ProjectId, Guid UserId, string Role) : IIntegrationEvent;

public sealed record MemberRemoved(Guid EventId, DateTimeOffset OccurredAt, long TenantId, Guid ProjectId, Guid UserId) : IIntegrationEvent;
