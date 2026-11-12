using FluentAssertions;
using NexUS.BuildingBlocks.Messaging;
using NexUS.BuildingBlocks.Messaging.Contracts;
using NexUS.User.Domain.Roles;
using NexUS.User.Domain.Users;
using NexUS.User.Infrastructure.Messaging;

namespace NexUS.User.UnitTests;

public class UserOutboxMapperTests
{
    private readonly UserOutboxMapper _mapper = new();

    [Fact]
    public void UserCreated_dipetakan_ke_kontrak_dengan_id_dan_waktu_dari_outbox()
    {
        var userId = Guid.NewGuid();
        var domainEvent = new UserCreatedDomainEvent(userId, 7, "budi@contoh.com", "Budi");
        var message = OutboxMessage.From(domainEvent);

        var mapped = _mapper.Map(message).Should().BeOfType<UserCreated>().Subject;

        mapped.Should().Be(new UserCreated(domainEvent.EventId, domainEvent.OccurredAt, 7, userId, "budi@contoh.com", "Budi"));
    }

    [Fact]
    public void Hanya_UserCreated_yang_diambil_relay_event_lain_tetap_tertunda_di_outbox()
    {
        _mapper.PublishedTypes.Should().Equal(typeof(UserCreatedDomainEvent).FullName);
        OutboxMessage.From(new UserCreatedDomainEvent(Guid.NewGuid(), 7, "a@b.c", "A")).Type
            .Should().StartWith(_mapper.PublishedTypes.Single() + ",");
    }

    [Fact]
    public void Event_lain_tidak_dipublikasikan()
    {
        var message = OutboxMessage.From(new RoleChangedDomainEvent(7, Guid.NewGuid(), null, RoleChangeReason.RolePermissionsChanged));

        _mapper.Map(message).Should().BeNull();
    }
}
