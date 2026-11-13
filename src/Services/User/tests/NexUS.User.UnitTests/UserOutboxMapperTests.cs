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
    public void UserUpdated_dan_UserDeactivated_dipetakan_ke_kontraknya()
    {
        var userId = Guid.NewGuid();
        var updated = new UserUpdatedDomainEvent(userId, 7, "budi@contoh.com", "Budi Baru", IsActive: false);
        var deactivated = new UserDeactivatedDomainEvent(userId, 7);

        _mapper.Map(OutboxMessage.From(updated)).Should()
            .Be(new UserUpdated(updated.EventId, updated.OccurredAt, 7, userId, "budi@contoh.com", "Budi Baru", false));
        _mapper.Map(OutboxMessage.From(deactivated)).Should()
            .Be(new UserDeactivated(deactivated.EventId, deactivated.OccurredAt, 7, userId));
    }

    [Fact]
    public void Payload_UserUpdated_lama_tanpa_IsActive_dianggap_aktif()
    {
        var message = OutboxMessage.From(new UserUpdatedDomainEvent(Guid.NewGuid(), 7, "a@b.c", "A"));
        message.Payload.Should().Contain("isActive");
        var legacy = System.Text.Json.JsonSerializer.Deserialize<UserUpdatedDomainEvent>(
            "{\"userId\":\"" + Guid.NewGuid() + "\",\"tenantId\":7,\"email\":\"a@b.c\",\"fullName\":\"A\"}",
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        legacy!.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Relay_hanya_mengambil_tipe_yang_punya_kontrak()
    {
        _mapper.PublishedTypes.Should().BeEquivalentTo(
        [
            typeof(UserCreatedDomainEvent).FullName, typeof(UserUpdatedDomainEvent).FullName, typeof(UserDeactivatedDomainEvent).FullName,
        ]);
        OutboxMessage.From(new UserCreatedDomainEvent(Guid.NewGuid(), 7, "a@b.c", "A")).Type
            .Should().StartWith(typeof(UserCreatedDomainEvent).FullName + ",");
    }

    [Fact]
    public void Event_lain_tidak_dipublikasikan()
    {
        var message = OutboxMessage.From(new RoleChangedDomainEvent(7, Guid.NewGuid(), null, RoleChangeReason.RolePermissionsChanged));

        _mapper.Map(message).Should().BeNull();
    }
}
