using System.Text.Json;
using NexUS.BuildingBlocks.Common.Domain;

namespace NexUS.BuildingBlocks.Messaging;

/// <summary>Pesan outbox yang disimpan dalam transaksi yang sama dengan perubahan data (relay di NX-028).</summary>
public sealed class OutboxMessage
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Guid Id { get; private init; }
    public string Type { get; private init; } = default!;
    public string Payload { get; private init; } = default!;
    public DateTimeOffset OccurredAt { get; private init; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public int RetryCount { get; private set; }
    public string? Error { get; private set; }

    private OutboxMessage() { } // untuk EF Core

    public static OutboxMessage From(IDomainEvent domainEvent) => new()
    {
        Id = domainEvent.EventId,
        Type = domainEvent.GetType().AssemblyQualifiedName!,
        Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), Json),
        OccurredAt = domainEvent.OccurredAt
    };

    public void MarkProcessed() => ProcessedAt = DateTimeOffset.UtcNow;

    public void MarkFailed(string error)
    {
        RetryCount++;
        Error = error;
    }
}
