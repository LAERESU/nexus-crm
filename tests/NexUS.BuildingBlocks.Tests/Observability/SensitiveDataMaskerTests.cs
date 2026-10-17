using FluentAssertions;
using NexUS.BuildingBlocks.Observability;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace NexUS.BuildingBlocks.Tests.Observability;

public class SensitiveDataMaskerTests
{
    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private sealed record LoginRequest(string UserName, string Password);

    private static (ILogger Logger, CapturingSink Sink) CreateLogger()
    {
        var sink = new CapturingSink();
        var logger = new LoggerConfiguration()
            .Enrich.With<SensitiveDataMasker>()
            .WriteTo.Sink(sink)
            .CreateLogger();
        return (logger, sink);
    }

    private static string Rendered(CapturingSink sink) => sink.Events.Single().RenderMessage();

    [Fact]
    public void Properti_bernama_password_atau_token_disamarkan()
    {
        var (logger, sink) = CreateLogger();

        logger.Information("Login {UserName} dengan {Password} dan {AccessToken}", "budi", "rahasia123", "eyJhbGciOi");

        var message = Rendered(sink);
        message.Should().Contain("budi").And.Contain(SensitiveDataMasker.Mask);
        message.Should().NotContain("rahasia123").And.NotContain("eyJhbGciOi");
    }

    [Fact]
    public void Anggota_objek_yang_di_destructure_disamarkan()
    {
        var (logger, sink) = CreateLogger();

        logger.Information("Permintaan {@Request}", new LoginRequest("budi", "rahasia123"));

        var message = Rendered(sink);
        message.Should().Contain("budi");
        message.Should().NotContain("rahasia123");
    }

    [Fact]
    public void Password_di_dalam_connection_string_disamarkan()
    {
        var (logger, sink) = CreateLogger();

        logger.Information("Koneksi {Target}", "User Id=TSK_SCHEMA;Password=App_Dev_2026;Data Source=oracle:1521/FREEPDB1");

        var message = Rendered(sink);
        message.Should().NotContain("App_Dev_2026");
        message.Should().Contain("User Id=TSK_SCHEMA").And.Contain("Data Source=oracle");
    }

    [Fact]
    public void Bearer_token_di_dalam_teks_disamarkan()
    {
        var (logger, sink) = CreateLogger();

        logger.Information("Header {Detail}", "Authorization gagal: Bearer abc.def.ghi");

        Rendered(sink).Should().NotContain("abc.def.ghi");
    }

    [Fact]
    public void Properti_biasa_tidak_diubah()
    {
        var (logger, sink) = CreateLogger();

        logger.Information("Tugas {TaskId} diubah oleh {UserId}", 42, "u-1");

        Rendered(sink).Should().Be("Tugas 42 diubah oleh \"u-1\"");
    }
}
