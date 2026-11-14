using Microsoft.EntityFrameworkCore;

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

public static class ProcessedEventModel
{
    /// <summary>Pemetaan tabel <c>PROCESSED_EVENTS</c> (dibuat migrasi baseline setiap schema).</summary>
    public static ModelBuilder MapProcessedEvents(this ModelBuilder model)
    {
        model.Entity<ProcessedEvent>(e =>
        {
            e.ToTable("PROCESSED_EVENTS");
            e.HasKey(x => new { x.EventId, x.Consumer });
            e.Property(x => x.EventId).HasColumnName("EVENT_ID");
            e.Property(x => x.Consumer).HasColumnName("CONSUMER").HasMaxLength(200);
            e.Property(x => x.ProcessedAt).HasColumnName("PROCESSED_AT");
        });
        return model;
    }
}
