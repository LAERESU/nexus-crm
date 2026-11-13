using System.Text.Json;
using NexUS.BuildingBlocks.Messaging;
using NexUS.BuildingBlocks.Messaging.Contracts;
using NexUS.User.Domain.Users;

namespace NexUS.User.Infrastructure.Messaging;

/// <summary>
/// Kontrak event User Service yang dipublikasikan ke service lain. Baru <c>UserCreated</c> (NX-021); event lain
/// (<c>UserDeactivated</c>, <c>RoleChanged</c>, ...) ditambahkan saat ada consumer-nya (NX-028) dan sampai saat itu
/// tetap tertunda di outbox.
/// </summary>
public sealed class UserOutboxMapper : IOutboxEventMapper
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public IReadOnlyCollection<string> PublishedTypes { get; } = [typeof(UserCreatedDomainEvent).FullName!];

    public IIntegrationEvent? Map(OutboxMessage message)
    {
        if (!IsType<UserCreatedDomainEvent>(message)) return null;

        var created = JsonSerializer.Deserialize<UserCreatedDomainEvent>(message.Payload, Json)
            ?? throw new JsonException("Payload UserCreated kosong.");
        // ID dan waktu diambil dari baris outbox: nilai di record domain dibuat ulang saat deserialisasi.
        return new UserCreated(message.Id, message.OccurredAt, created.TenantId, created.UserId, created.Email, created.FullName);
    }

    private static bool IsType<T>(OutboxMessage message) =>
        message.Type.StartsWith(typeof(T).FullName + ",", StringComparison.Ordinal);
}
