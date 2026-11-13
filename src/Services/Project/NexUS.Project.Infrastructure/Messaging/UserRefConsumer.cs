using Microsoft.EntityFrameworkCore;
using NexUS.BuildingBlocks.Messaging;
using NexUS.BuildingBlocks.Messaging.Contracts;
using NexUS.Project.Domain.Users;
using NexUS.Project.Infrastructure.Persistence;

namespace NexUS.Project.Infrastructure.Messaging;

/// <summary>
/// Memperbarui read model user (<c>USER_REF</c>) dari event User Service. Idempotent: event yang sudah tercatat di
/// <c>PROCESSED_EVENTS</c> dilewati, dan catatan itu disimpan dalam transaksi yang sama dengan perubahan read model.
/// Ketiga event datang lewat antrean berbeda sehingga bisa tiba tidak berurutan; <see cref="UserRef"/> mengabaikan
/// event yang lebih lama daripada yang sudah diterapkan.
/// </summary>
public sealed class UserRefConsumer(ProjectDbContext db, TimeProvider timeProvider)
    : IIntegrationEventHandler<UserCreated>, IIntegrationEventHandler<UserUpdated>, IIntegrationEventHandler<UserDeactivated>
{
    public Task HandleAsync(UserCreated e, CancellationToken ct) =>
        ApplyAsync(e, "UserCreated", e.TenantId, e.UserId, user => user.ApplyProfile(e.Email, e.FullName, isActive: true, e.OccurredAt), ct);

    public Task HandleAsync(UserUpdated e, CancellationToken ct) =>
        ApplyAsync(e, "UserUpdated", e.TenantId, e.UserId, user => user.ApplyProfile(e.Email, e.FullName, e.IsActive, e.OccurredAt), ct);

    public Task HandleAsync(UserDeactivated e, CancellationToken ct) =>
        ApplyAsync(e, "UserDeactivated", e.TenantId, e.UserId, user => user.Deactivate(e.OccurredAt), ct);

    private async Task ApplyAsync(
        IIntegrationEvent integrationEvent, string eventName, long tenantId, Guid userId, Func<UserRef, bool> apply, CancellationToken ct)
    {
        var consumer = $"{DependencyInjection.ServiceName}.{eventName}";
        if (await db.ProcessedEvents.AnyAsync(p => p.EventId == integrationEvent.EventId && p.Consumer == consumer, ct)) return;

        // Consumer berjalan tanpa user/request, jadi filter tenant dilewati dan tenant diberikan eksplisit.
        var user = await db.UserRefs.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.TenantId == tenantId && u.UserId == userId, ct);
        if (user is null)
        {
            user = UserRef.Create(tenantId, userId);
            db.UserRefs.Add(user);
        }

        apply(user);
        db.ProcessedEvents.Add(ProcessedEvent.Create(integrationEvent.EventId, consumer, timeProvider.GetUtcNow()));

        // Bentrok (baris yang sama dibuat/diubah consumer lain) melempar exception: pesan dikembalikan ke antrean dan diulang.
        await db.SaveChangesAsync(ct);
    }
}
