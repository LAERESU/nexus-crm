namespace NexUS.BuildingBlocks.Messaging;

/// <summary>Event yang dikirim antar service lewat RabbitMQ. Consumer wajib idempotent berdasarkan <see cref="EventId"/>.</summary>
public interface IIntegrationEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}
