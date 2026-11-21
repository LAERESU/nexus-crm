using Microsoft.EntityFrameworkCore;
using NexUS.BuildingBlocks.Common.Domain;

namespace NexUS.BuildingBlocks.Messaging;

/// <summary>
/// Outbox pattern (ARCHITECTURE.md D.4) yang dipakai semua DbContext service: kolom audit dan <c>ROW_VERSION</c> diisi,
/// lalu domain event disimpan ke <c>OUTBOX_MESSAGES</c> dalam transaksi yang sama dengan perubahan data.
/// </summary>
public static class DomainEventOutbox
{
    /// <summary>Dipanggil di awal <c>SaveChangesAsync</c>, sebelum <c>base.SaveChangesAsync</c>; mengembalikan domain event yang disimpan.</summary>
    /// <param name="actor">Isi kolom CREATED_BY/UPDATED_BY.</param>
    public static IReadOnlyList<IDomainEvent> PrepareSave(this DbContext db, DateTimeOffset now, long actor)
    {
        // Perubahan entitas anak (mis. anggota proyek) ikut menaikkan ROW_VERSION aggregate-nya, sehingga perubahan anak
        // yang bersamaan tidak bisa sama-sama lolos: yang kedua bentrok versi (409).
        var childChanged = db.ChangeTracker.Entries<AggregateRoot>().Where(e => e.Entity.HasChildChanges()).ToList();
        foreach (var root in childChanged)
        {
            if (root.State == EntityState.Unchanged) root.State = EntityState.Modified;
            // Tanda bisa langsung dibersihkan: bila simpan gagal, status Modified tetap tercatat di change tracker.
            root.Entity.AcceptChildChanges();
        }

        foreach (var entry in db.ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.CreatedBy = actor;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedBy = actor;
                entry.Entity.RowVersion++;
            }
        }

        var events = db.ChangeTracker.Entries<AggregateRoot>().SelectMany(e => e.Entity.PopDomainEvents()).ToList();
        db.Set<OutboxMessage>().AddRange(events.Select(OutboxMessage.From));

        return events;
    }

    /// <summary>
    /// Menetapkan versi yang diharapkan klien (header <c>If-Match</c>, CONTEXT.md bagian 6). Dicek langsung, bukan hanya
    /// lewat klausa UPDATE: bila tidak ada perubahan (PUT yang sama) tidak ada UPDATE, tetapi klien dengan versi basi tetap
    /// berhak mendapat 409.
    /// </summary>
    public static void ExpectRowVersionOf(this DbContext db, object entity, long expectedRowVersion)
    {
        var rowVersion = db.Entry(entity).Property(nameof(Entity.RowVersion));
        if ((long)rowVersion.CurrentValue! != expectedRowVersion)
            throw new NexUS.BuildingBlocks.Common.Exceptions.ConcurrencyException("Data sudah diubah oleh proses lain; muat ulang lalu coba lagi.");

        rowVersion.OriginalValue = expectedRowVersion;
    }

    /// <summary>Pemetaan tabel <c>OUTBOX_MESSAGES</c> (dibuat migrasi baseline setiap schema).</summary>
    public static ModelBuilder MapOutboxMessages(this ModelBuilder model)
    {
        model.Entity<OutboxMessage>(e =>
        {
            e.ToTable("OUTBOX_MESSAGES");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("ID").ValueGeneratedNever();
            e.Property(x => x.Type).HasColumnName("TYPE").HasMaxLength(500);
            e.Property(x => x.Payload).HasColumnName("PAYLOAD").HasColumnType("CLOB");
            e.Property(x => x.OccurredAt).HasColumnName("OCCURRED_AT");
            e.Property(x => x.ProcessedAt).HasColumnName("PROCESSED_AT");
            e.Property(x => x.RetryCount).HasColumnName("RETRY_COUNT");
            e.Property(x => x.Error).HasColumnName("ERROR").HasMaxLength(2000);
        });
        return model;
    }
}
