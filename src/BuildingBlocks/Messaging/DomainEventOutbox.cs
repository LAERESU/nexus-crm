using Microsoft.EntityFrameworkCore;
using NexUS.BuildingBlocks.Common.Domain;

namespace NexUS.BuildingBlocks.Messaging;

/// <summary>Hasil <see cref="DomainEventOutbox.PrepareSave"/>; panggil <see cref="Complete"/> setelah simpan berhasil.</summary>
public sealed record PreparedSave(IReadOnlyList<IDomainEvent> Events, IReadOnlyList<AggregateRoot> ChildChangedRoots)
{
    public void Complete()
    {
        foreach (var root in ChildChangedRoots) root.AcceptChildChanges();
    }
}

/// <summary>
/// Outbox pattern (ARCHITECTURE.md D.4) yang dipakai semua DbContext service: kolom audit dan <c>ROW_VERSION</c> diisi,
/// lalu domain event disimpan ke <c>OUTBOX_MESSAGES</c> dalam transaksi yang sama dengan perubahan data.
/// </summary>
public static class DomainEventOutbox
{
    /// <summary>Dipanggil di awal <c>SaveChangesAsync</c>, sebelum <c>base.SaveChangesAsync</c>.</summary>
    /// <param name="actor">Isi kolom CREATED_BY/UPDATED_BY.</param>
    public static PreparedSave PrepareSave(this DbContext db, DateTimeOffset now, long actor)
    {
        // Perubahan entitas anak (mis. anggota proyek) ikut menaikkan ROW_VERSION aggregate-nya, sehingga perubahan anak
        // yang bersamaan tidak bisa sama-sama lolos: yang kedua bentrok versi (409).
        var childChanged = db.ChangeTracker.Entries<AggregateRoot>().Where(e => e.Entity.HasChildChanges()).ToList();
        foreach (var root in childChanged.Where(e => e.State == EntityState.Unchanged))
            root.State = EntityState.Modified;

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

        return new PreparedSave(events, childChanged.Select(e => e.Entity).ToList());
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
