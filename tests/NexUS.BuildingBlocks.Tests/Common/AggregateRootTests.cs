using FluentAssertions;
using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Messaging;

namespace NexUS.BuildingBlocks.Tests.Common;

public class AggregateRootTests
{
    private sealed record SampleEvent(string Name) : DomainEvent;

    private sealed class SampleAggregate : AggregateRoot
    {
        public void Raise(string name) => AddDomainEvent(new SampleEvent(name));
    }

    [Fact]
    public void Entity_baru_punya_PublicId_dan_belum_dihapus()
    {
        var aggregate = new SampleAggregate();

        aggregate.PublicId.Should().NotBeEmpty();
        aggregate.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void MarkDeleted_menandai_soft_delete()
    {
        var aggregate = new SampleAggregate();

        aggregate.MarkDeleted();

        aggregate.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public void PopDomainEvents_mengembalikan_event_lalu_mengosongkan_daftar()
    {
        var aggregate = new SampleAggregate();
        aggregate.Raise("a");
        aggregate.Raise("b");

        var events = aggregate.PopDomainEvents();

        events.Should().HaveCount(2);
        aggregate.DomainEvents.Should().BeEmpty();
        aggregate.PopDomainEvents().Should().BeEmpty();
    }

    [Fact]
    public void DomainEvent_punya_EventId_unik()
    {
        new SampleEvent("a").EventId.Should().NotBe(new SampleEvent("a").EventId);
    }

    [Fact]
    public void OutboxMessage_From_menyalin_id_tipe_dan_payload_event()
    {
        var domainEvent = new SampleEvent("halo");

        var message = OutboxMessage.From(domainEvent);

        message.Id.Should().Be(domainEvent.EventId);
        message.Type.Should().Contain(nameof(SampleEvent));
        message.Payload.Should().Contain("halo");
        message.ProcessedAt.Should().BeNull();
    }

    [Fact]
    public void OutboxMessage_MarkFailed_menambah_retry_dan_MarkProcessed_mengisi_waktu()
    {
        var message = OutboxMessage.From(new SampleEvent("x"));

        message.MarkFailed("gagal kirim");
        message.MarkProcessed();

        message.RetryCount.Should().Be(1);
        message.Error.Should().Be("gagal kirim");
        message.ProcessedAt.Should().NotBeNull();
    }
}
