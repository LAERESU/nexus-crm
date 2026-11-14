using NexUS.BuildingBlocks.Messaging;
using NexUS.BuildingBlocks.Messaging.Contracts;
using NexUS.User.Domain.Roles;
using NexUS.User.Domain.Users;

namespace NexUS.User.Infrastructure.Messaging;

/// <summary>
/// Kontrak event User Service yang dipublikasikan ke service lain: <c>UserCreated</c> (NX-021), <c>UserUpdated</c> dan
/// <c>UserDeactivated</c> (NX-026), serta <c>RoleChanged</c> (NX-028). Event tanpa antrean penerima diparkir oleh
/// alternate exchange (ADR-0012).
/// </summary>
public sealed class UserOutboxMapper : OutboxEventMapper
{
    public UserOutboxMapper()
    {
        Map<UserCreatedDomainEvent>((e, id, at) => new UserCreated(id, at, e.TenantId, e.UserId, e.Email, e.FullName));
        Map<UserUpdatedDomainEvent>((e, id, at) => new UserUpdated(id, at, e.TenantId, e.UserId, e.Email, e.FullName, e.IsActive));
        Map<UserDeactivatedDomainEvent>((e, id, at) => new UserDeactivated(id, at, e.TenantId, e.UserId));
        Map<RoleChangedDomainEvent>((e, id, at) => new RoleChanged(id, at, e.TenantId, e.RoleId, e.UserId, e.Reason.ToString()));
    }
}
