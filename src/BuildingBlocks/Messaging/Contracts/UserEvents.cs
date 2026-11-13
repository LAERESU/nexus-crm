namespace NexUS.BuildingBlocks.Messaging.Contracts;

/// <summary>User baru dibuat di User Service. Auth membuat kredensial awal dan mengirim undangan aktivasi (NX-021).</summary>
public sealed record UserCreated(Guid EventId, DateTimeOffset OccurredAt, long TenantId, Guid UserId, string Email, string FullName)
    : IIntegrationEvent;

/// <summary>
/// Profil user berubah atau user diaktifkan kembali. Service lain memperbarui read model user miliknya (NX-026).
/// </summary>
public sealed record UserUpdated(
    Guid EventId, DateTimeOffset OccurredAt, long TenantId, Guid UserId, string Email, string FullName, bool IsActive)
    : IIntegrationEvent;

/// <summary>User dinonaktifkan. Service lain menandai read model user miliknya sebagai nonaktif (NX-026).</summary>
public sealed record UserDeactivated(Guid EventId, DateTimeOffset OccurredAt, long TenantId, Guid UserId) : IIntegrationEvent;
