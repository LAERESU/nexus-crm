namespace NexUS.BuildingBlocks.Messaging;

/// <summary>
/// Catatan event yang sudah diproses satu consumer (tabel <c>PROCESSED_EVENTS</c>). Disimpan dalam transaksi yang sama
/// dengan efek event, sehingga event yang datang dua kali tidak menghasilkan efek ganda (CONTEXT.md bagian 3 poin 3).
/// </summary>
public sealed class ProcessedEvent
{
    public Guid EventId { get; private init; }
    public string Consumer { get; private init; } = default!;
    public DateTimeOffset ProcessedAt { get; private init; }

    private ProcessedEvent() { } // untuk EF Core

    public static ProcessedEvent Create(Guid eventId, string consumer, DateTimeOffset now) =>
        new() { EventId = eventId, Consumer = consumer, ProcessedAt = now };
}
