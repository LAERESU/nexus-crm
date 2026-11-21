using Microsoft.EntityFrameworkCore;
using NexUS.BuildingBlocks.Messaging;
using NexUS.BuildingBlocks.Messaging.Contracts;
using NexUS.Tasks.Domain.Projects;
using NexUS.Tasks.Infrastructure.Persistence;

namespace NexUS.Tasks.Infrastructure.Messaging;

/// <summary>
/// Memperbarui read model proyek (<c>PROJECT_REF</c>, <c>PROJECT_MEMBER_REF</c>) dari event Project Service, idempotent
/// lewat <see cref="IdempotentEventHandler{TEvent, TContext}"/>. Event berbeda jenis bisa tiba tidak berurutan; entitas
/// read model mengabaikan event yang lebih lama. Bentrok membuat baris yang sama melempar exception dan pesan diulang.
/// </summary>
public abstract class ProjectRefHandler<TEvent>(TaskDbContext db, TimeProvider timeProvider, EventConsumerName<TEvent> consumer)
    : IdempotentEventHandler<TEvent, TaskDbContext>(db, timeProvider, consumer) where TEvent : IIntegrationEvent
{
    // Consumer berjalan tanpa user/request, jadi filter tenant dilewati dan tenant diberikan eksplisit.

    protected async Task<ProjectRef> ProjectAsync(long tenantId, Guid projectId, CancellationToken ct)
    {
        var project = await Db.ProjectRefs.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.ProjectId == projectId, ct);
        if (project is not null) return project;

        project = ProjectRef.Create(tenantId, projectId);
        Db.ProjectRefs.Add(project);
        return project;
    }

    protected async Task<ProjectMemberRef> MemberAsync(long tenantId, Guid projectId, Guid userId, CancellationToken ct)
    {
        var member = await Db.ProjectMemberRefs.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.ProjectId == projectId && m.UserId == userId, ct);
        if (member is not null) return member;

        member = ProjectMemberRef.Create(tenantId, projectId, userId);
        Db.ProjectMemberRefs.Add(member);
        return member;
    }
}

/// <summary>Proyek baru; pembuatnya otomatis manager (Project Service tidak mengirim <c>MemberAdded</c> untuknya).</summary>
public sealed class ProjectCreatedHandler(TaskDbContext db, TimeProvider timeProvider, EventConsumerName<ProjectCreated> consumer)
    : ProjectRefHandler<ProjectCreated>(db, timeProvider, consumer)
{
    protected override async Task ApplyAsync(ProjectCreated e, CancellationToken ct)
    {
        (await ProjectAsync(e.TenantId, e.ProjectId, ct)).ApplyDetails(e.Code, e.Name, e.OccurredAt);
        (await MemberAsync(e.TenantId, e.ProjectId, e.OwnerId, ct)).ApplyRole(ProjectMemberRef.Manager, e.OccurredAt);
    }
}

public sealed class ProjectUpdatedHandler(TaskDbContext db, TimeProvider timeProvider, EventConsumerName<ProjectUpdated> consumer)
    : ProjectRefHandler<ProjectUpdated>(db, timeProvider, consumer)
{
    protected override async Task ApplyAsync(ProjectUpdated e, CancellationToken ct) =>
        (await ProjectAsync(e.TenantId, e.ProjectId, ct)).ApplyDetails(e.Code, e.Name, e.OccurredAt);
}

public sealed class ProjectArchivedHandler(TaskDbContext db, TimeProvider timeProvider, EventConsumerName<ProjectArchived> consumer)
    : ProjectRefHandler<ProjectArchived>(db, timeProvider, consumer)
{
    protected override async Task ApplyAsync(ProjectArchived e, CancellationToken ct) =>
        (await ProjectAsync(e.TenantId, e.ProjectId, ct)).Archive();
}

public sealed class MemberAddedHandler(TaskDbContext db, TimeProvider timeProvider, EventConsumerName<MemberAdded> consumer)
    : ProjectRefHandler<MemberAdded>(db, timeProvider, consumer)
{
    protected override async Task ApplyAsync(MemberAdded e, CancellationToken ct) =>
        (await MemberAsync(e.TenantId, e.ProjectId, e.UserId, ct)).ApplyRole(e.Role, e.OccurredAt);
}

public sealed class MemberRoleChangedHandler(TaskDbContext db, TimeProvider timeProvider, EventConsumerName<MemberRoleChanged> consumer)
    : ProjectRefHandler<MemberRoleChanged>(db, timeProvider, consumer)
{
    protected override async Task ApplyAsync(MemberRoleChanged e, CancellationToken ct) =>
        (await MemberAsync(e.TenantId, e.ProjectId, e.UserId, ct)).ApplyRole(e.Role, e.OccurredAt);
}

public sealed class MemberRemovedHandler(TaskDbContext db, TimeProvider timeProvider, EventConsumerName<MemberRemoved> consumer)
    : ProjectRefHandler<MemberRemoved>(db, timeProvider, consumer)
{
    protected override async Task ApplyAsync(MemberRemoved e, CancellationToken ct) =>
        (await MemberAsync(e.TenantId, e.ProjectId, e.UserId, ct)).Remove(e.OccurredAt);
}
