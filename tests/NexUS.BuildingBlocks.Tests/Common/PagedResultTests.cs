using FluentAssertions;
using NexUS.BuildingBlocks.Common.Results;

namespace NexUS.BuildingBlocks.Tests.Common;

public class PagedResultTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(3, 3)]
    public void NormalizePage_minimal_1(int input, int expected) =>
        PagedResult<int>.NormalizePage(input).Should().Be(expected);

    [Theory]
    [InlineData(0, 20)]
    [InlineData(-1, 20)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(500, 100)]
    public void NormalizeSize_default_20_dan_maksimum_100(int input, int expected) =>
        PagedResult<int>.NormalizeSize(input).Should().Be(expected);

    [Fact]
    public void Skip_dihitung_dari_page_dan_size_yang_sudah_dinormalisasi()
    {
        PagedResult<int>.Skip(1, 20).Should().Be(0);
        PagedResult<int>.Skip(3, 20).Should().Be(40);
        PagedResult<int>.Skip(2, 500).Should().Be(100);
    }

    [Fact]
    public void Menyimpan_item_dan_total()
    {
        var result = new PagedResult<int>([1, 2], 1, 2, 10);

        result.Items.Should().Equal(1, 2);
        result.TotalCount.Should().Be(10);
    }
}
