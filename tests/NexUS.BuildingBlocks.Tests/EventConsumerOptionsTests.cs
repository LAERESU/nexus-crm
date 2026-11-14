using FluentAssertions;
using NexUS.BuildingBlocks.Messaging;

namespace NexUS.BuildingBlocks.Tests;

public class EventConsumerOptionsTests
{
    [Fact]
    public void Jeda_berlipat_dua_sampai_batas_dan_bawaan_cukup_untuk_gangguan_sementara()
    {
        var options = new EventConsumerOptions();

        Enumerable.Range(1, 6).Select(options.DelayFor).Should().Equal(
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(40),
            TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
        options.DelayFor(1000).Should().Be(TimeSpan.FromSeconds(60));

        var total = Enumerable.Range(1, options.MaxAttempts - 1).Aggregate(TimeSpan.Zero, (sum, n) => sum + options.DelayFor(n));
        total.Should().BeGreaterThan(TimeSpan.FromMinutes(15), "gangguan database/SMTP belasan menit tidak membuang event");
    }
}
