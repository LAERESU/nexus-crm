namespace NexUS.BuildingBlocks.Messaging.Contracts;

/// <summary>User baru dibuat di User Service. Auth membuat kredensial awal dan mengirim undangan aktivasi (NX-021).</summary>
public sealed record UserCreated(Guid EventId, DateTimeOffset OccurredAt, long TenantId, Guid UserId, string Email, string FullName)
    : IIntegrationEvent;
