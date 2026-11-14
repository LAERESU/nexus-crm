using System.Text.Json;
using NexUS.BuildingBlocks.Messaging;
using NexUS.BuildingBlocks.Messaging.Contracts;
using NexUS.User.Domain.Users;

namespace NexUS.User.Infrastructure.Messaging;

/// <summary>
/// Kontrak event User Service yang dipublikasikan ke service lain: <c>UserCreated</c> (NX-021), <c>UserUpdated</c>, dan
/// <c>UserDeactivated</c> (NX-026). Event lain (<c>RoleChanged</c>, ...) ditambahkan saat ada consumer-nya dan sampai
/// saat itu tetap tertunda di outbox.
/// </summary>
public sealed class UserOutboxMapper : IOutboxEventMapper
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public IReadOnlyCollection<string> PublishedTypes { get; } =
    [
        typeof(UserCreatedDomainEvent).FullName!,
        typeof(UserUpdatedDomainEvent).FullName!,
        typeof(UserDeactivatedDomainEvent).FullName!,
    ];

    // ID dan waktu diambil dari baris outbox: nilai di record domain dibuat ulang saat deserialisasi.
    public IIntegrationEvent? Map(OutboxMessage message)
    {
        if (IsType<UserCreatedDomainEvent>(message))
        {
            var created = Read<UserCreatedDomainEvent>(message);
            return new UserCreated(message.Id, message.OccurredAt, created.TenantId, created.UserId, created.Email, created.FullName);
        }

        if (IsType<UserUpdatedDomainEvent>(message))
        {
            var updated = Read<UserUpdatedDomainEvent>(message);
            return new UserUpdated(
                message.Id, message.OccurredAt, updated.TenantId, updated.UserId, updated.Email, updated.FullName, updated.IsActive);
        }

        if (IsType<UserDeactivatedDomainEvent>(message))
        {
            var deactivated = Read<UserDeactivatedDomainEvent>(message);
            return new UserDeactivated(message.Id, message.OccurredAt, deactivated.TenantId, deactivated.UserId);
        }

        return null;
    }

    private static T Read<T>(OutboxMessage message) =>
        JsonSerializer.Deserialize<T>(message.Payload, Json) ?? throw new JsonException($"Payload {typeof(T).Name} kosong.");

    private static bool IsType<T>(OutboxMessage message) =>
        message.Type.StartsWith(typeof(T).FullName + ",", StringComparison.Ordinal);
}
