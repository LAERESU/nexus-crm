using Microsoft.EntityFrameworkCore;

namespace NexUS.BuildingBlocks.Messaging;

/// <summary>
/// Dasar consumer idempotent (CONTEXT.md bagian 3 poin 3): event yang sudah tercatat di <c>PROCESSED_EVENTS</c> untuk
/// consumer ini dilewati, dan catatan itu disimpan dalam transaksi yang sama dengan efek event. Bila dua pengiriman
/// event yang sama diproses bersamaan, yang kedua gagal pada primary key <c>PROCESSED_EVENTS</c>, efeknya ikut batal, dan
/// saat diulang event itu dilewati.
/// </summary>
/// <typeparam name="TContext">DbContext service yang memetakan <see cref="ProcessedEvent"/> (lihat <c>MapProcessedEvents</c>).</typeparam>
public abstract class IdempotentEventHandler<TEvent, TContext>(TContext db, TimeProvider timeProvider, EventConsumerName<TEvent> consumer)
    : IIntegrationEventHandler<TEvent>
    where TEvent : IIntegrationEvent
    where TContext : DbContext
{
    protected TContext Db => db;

    /// <summary>Nama consumer di <c>PROCESSED_EVENTS</c>, sama dengan nama antreannya (dari <c>AddNexusEventConsumer</c>).</summary>
    public string ConsumerName => consumer.Value;

    public async Task HandleAsync(TEvent integrationEvent, CancellationToken ct)
    {
        var processed = db.Set<ProcessedEvent>();
        if (await processed.AnyAsync(p => p.EventId == integrationEvent.EventId && p.Consumer == ConsumerName, ct)) return;

        await ApplyAsync(integrationEvent, ct);
        processed.Add(ProcessedEvent.Create(integrationEvent.EventId, ConsumerName, timeProvider.GetUtcNow()));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Menerapkan efek event ke <see cref="Db"/> tanpa menyimpan; penyimpanan dilakukan bersama catatan idempotensi.</summary>
    protected abstract Task ApplyAsync(TEvent integrationEvent, CancellationToken ct);
}

/// <summary>Nama consumer satu jenis event (<c>{service}.{NamaEvent}</c>), didaftarkan oleh <c>AddNexusEventConsumer</c>.</summary>
public sealed record EventConsumerName<TEvent>(string Value) where TEvent : IIntegrationEvent;
