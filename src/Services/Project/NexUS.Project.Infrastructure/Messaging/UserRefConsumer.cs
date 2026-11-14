using Microsoft.EntityFrameworkCore;
using NexUS.BuildingBlocks.Messaging;
using NexUS.BuildingBlocks.Messaging.Contracts;
using NexUS.Project.Domain.Users;
using NexUS.Project.Infrastructure.Persistence;

namespace NexUS.Project.Infrastructure.Messaging;

/// <summary>
/// Memperbarui read model user (<c>USER_REF</c>) dari event User Service, idempotent lewat
/// <see cref="IdempotentEventHandler{TEvent, TContext}"/>. Ketiga event datang lewat antrean berbeda sehingga bisa tiba
/// tidak berurutan; <see cref="UserRef"/> mengabaikan event yang lebih lama daripada yang sudah diterapkan.
/// Bentrok (baris yang sama dibuat consumer lain) melempar exception: pesan diulang oleh consumer.
/// </summary>
public abstract class UserRefHandler<TEvent>(ProjectDbContext db, TimeProvider timeProvider, EventConsumerName<TEvent> consumer)
    : IdempotentEventHandler<TEvent, ProjectDbContext>(db, timeProvider, consumer) where TEvent : IIntegrationEvent
{
    protected async Task<UserRef> FindOrCreateAsync(long tenantId, Guid userId, CancellationToken ct)
    {
        // Consumer berjalan tanpa user/request, jadi filter tenant dilewati dan tenant diberikan eksplisit.
        var user = await Db.UserRefs.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.TenantId == tenantId && u.UserId == userId, ct);
        if (user is not null) return user;

        user = UserRef.Create(tenantId, userId);
        Db.UserRefs.Add(user);
        return user;
    }
}

public sealed class UserCreatedRefHandler(ProjectDbContext db, TimeProvider timeProvider, EventConsumerName<UserCreated> consumer)
    : UserRefHandler<UserCreated>(db, timeProvider, consumer)
{
    protected override async Task ApplyAsync(UserCreated e, CancellationToken ct) =>
        (await FindOrCreateAsync(e.TenantId, e.UserId, ct)).ApplyProfile(e.Email, e.FullName, isActive: true, e.OccurredAt);
}

public sealed class UserUpdatedRefHandler(ProjectDbContext db, TimeProvider timeProvider, EventConsumerName<UserUpdated> consumer)
    : UserRefHandler<UserUpdated>(db, timeProvider, consumer)
{
    protected override async Task ApplyAsync(UserUpdated e, CancellationToken ct) =>
        (await FindOrCreateAsync(e.TenantId, e.UserId, ct)).ApplyProfile(e.Email, e.FullName, e.IsActive, e.OccurredAt);
}

public sealed class UserDeactivatedRefHandler(ProjectDbContext db, TimeProvider timeProvider, EventConsumerName<UserDeactivated> consumer)
    : UserRefHandler<UserDeactivated>(db, timeProvider, consumer)
{
    protected override async Task ApplyAsync(UserDeactivated e, CancellationToken ct) =>
        (await FindOrCreateAsync(e.TenantId, e.UserId, ct)).Deactivate(e.OccurredAt);
}
