using FluentAssertions;
using NexUS.BuildingBlocks.Messaging;
using NexUS.BuildingBlocks.Messaging.Contracts;
using NexUS.Project.Domain.Milestones;
using NexUS.Project.Domain.Projects;
using NexUS.Project.Infrastructure.Messaging;

namespace NexUS.Project.UnitTests;

public class ProjectOutboxMapperTests
{
    private readonly ProjectOutboxMapper _mapper = new();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private IIntegrationEvent? Map(NexUS.BuildingBlocks.Common.Domain.IDomainEvent domainEvent) => _mapper.Map(OutboxMessage.From(domainEvent));

    [Fact]
    public void Event_proyek_dan_anggota_dipetakan_ke_kontrak_dengan_id_dan_waktu_dari_outbox()
    {
        var created = new ProjectCreatedDomainEvent(7, ProjectId, "WEB", "Portal", UserId);
        Map(created).Should().Be(new ProjectCreated(created.EventId, created.OccurredAt, 7, ProjectId, "WEB", "Portal", UserId));

        var updated = new ProjectUpdatedDomainEvent(7, ProjectId, "WEB", "Portal", "ON_HOLD");
        Map(updated).Should().Be(new ProjectUpdated(updated.EventId, updated.OccurredAt, 7, ProjectId, "WEB", "Portal", "on_hold"));

        var archived = new ProjectArchivedDomainEvent(7, ProjectId);
        Map(archived).Should().Be(new ProjectArchived(archived.EventId, archived.OccurredAt, 7, ProjectId));

        var added = new ProjectMemberAddedDomainEvent(7, ProjectId, UserId, "VIEWER");
        Map(added).Should().Be(new MemberAdded(added.EventId, added.OccurredAt, 7, ProjectId, UserId, "viewer"));

        var changed = new ProjectMemberRoleChangedDomainEvent(7, ProjectId, UserId, "MANAGER");
        Map(changed).Should().Be(new MemberRoleChanged(changed.EventId, changed.OccurredAt, 7, ProjectId, UserId, "manager"));

        var removed = new ProjectMemberRemovedDomainEvent(7, ProjectId, UserId);
        Map(removed).Should().Be(new MemberRemoved(removed.EventId, removed.OccurredAt, 7, ProjectId, UserId));
    }

    [Fact]
    public void Event_milestone_belum_punya_kontrak_dan_tetap_tertunda()
    {
        Map(new MilestoneCreatedDomainEvent(7, ProjectId, Guid.NewGuid(), "M", new DateOnly(2026, 12, 1))).Should().BeNull();
        _mapper.PublishedTypes.Should().HaveCount(6).And.NotContain(typeof(MilestoneCreatedDomainEvent).FullName);
    }
}
