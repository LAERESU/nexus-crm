using FluentAssertions;
using NexUS.BuildingBlocks.Common.Results;

namespace NexUS.BuildingBlocks.Tests.Common;

public class ResultTests
{
    [Fact]
    public void Success_membawa_nilai_dan_tanpa_error()
    {
        var result = Result.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Error.Should().Be(Error.None);
        result.Value.Should().Be(42);
    }

    [Fact]
    public void Failure_membawa_error_dan_nilai_tidak_bisa_dibaca()
    {
        var error = Error.NotFound("Tugas tidak ditemukan.");

        var result = Result.Failure<int>(error);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
        result.Invoking(r => r.Value).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Failure_tanpa_error_ditolak()
    {
        var act = () => Result.Failure(Error.None);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Nilai_bisa_dikonversi_implisit_menjadi_hasil_sukses()
    {
        Result<string> result = "ok";

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("ok");
    }
}
