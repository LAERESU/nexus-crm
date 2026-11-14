namespace NexUS.BuildingBlocks.Messaging.Contracts;

/// <summary>User baru dibuat di User Service. Auth membuat kredensial awal dan mengirim undangan aktivasi (NX-021).</summary>
public sealed record UserCreated(Guid EventId, DateTimeOffset OccurredAt, long TenantId, Guid UserId, string Email, string FullName)
    : IIntegrationEvent;

/// <summary>
/// Profil user berubah atau user diaktifkan kembali. Service lain memperbarui read model user miliknya (NX-026).
/// <c>IsActive</c> null bila event berasal dari outbox sebelum status disertakan; consumer tidak mengubah status.
/// </summary>
public sealed record UserUpdated(
    Guid EventId, DateTimeOffset OccurredAt, long TenantId, Guid UserId, string Email, string FullName, bool? IsActive)
    : IIntegrationEvent;

/// <summary>User dinonaktifkan. Service lain menandai read model user miliknya sebagai nonaktif (NX-026).</summary>
public sealed record UserDeactivated(Guid EventId, DateTimeOffset OccurredAt, long TenantId, Guid UserId) : IIntegrationEvent;

/// <summary>
/// Role atau penugasan role berubah (NX-028). <c>RoleId</c> diisi bila permission role berubah, <c>UserId</c> bila
/// penugasan atau status user berubah. <c>Reason</c>: <c>role_permissions_changed</c>, <c>user_roles_changed</c>, atau
/// <c>user_status_changed</c>. Cache permission di Redis sudah dihapus langsung oleh User Service (ADR-0009).
/// </summary>
public sealed record RoleChanged(Guid EventId, DateTimeOffset OccurredAt, long TenantId, Guid? RoleId, Guid? UserId, string Reason)
    : IIntegrationEvent;
