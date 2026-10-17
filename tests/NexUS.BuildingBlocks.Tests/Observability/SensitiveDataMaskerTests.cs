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

        logger.Information("Header {Detail}", "Authorization gagal: Bearer eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl");

        Rendered(sink).Should().NotContain("eyJhbGciOiJSUzI1NiJ9");
    }

    [Fact]
    public void Pola_token_apikey_json_dan_basic_di_dalam_teks_disamarkan()
    {
        var (logger, sink) = CreateLogger();

        logger.Information("A {A} B {B} C {C}",
            "gagal: token=abc123xyz apikey=kunci-rahasia",
            "{\"password\":\"p@ss\",\"user\":\"budi\"}",
            "Authorization: Basic dXNlcjpwYXNzd29yZA==");

        var message = Rendered(sink);
        message.Should().NotContain("abc123xyz").And.NotContain("kunci-rahasia")
            .And.NotContain("p@ss").And.NotContain("dXNlcjpwYXNzd29yZA");
        message.Should().Contain("budi");
    }

    [Theory]
    [InlineData("CancellationToken")]
    [InlineData("AuthorizationPolicy")]
    [InlineData("TokenCount")]
    [InlineData("ConnectionStringName")]
    public void Nama_properti_yang_hanya_mirip_tidak_disamarkan(string name)
    {
        var (logger, sink) = CreateLogger();
        logger.Write(LogEventLevel.Information, "{" + name + "}", "nilai-biasa");

        Rendered(sink).Should().Contain("nilai-biasa");
    }

    [Theory]
    [InlineData("AccessToken")]
    [InlineData("RefreshToken")]
    [InlineData("Token")]
    [InlineData("DbPassword")]
    [InlineData("ClientSecret")]
    public void Nama_properti_sensitif_disamarkan(string name)
    {
        var (logger, sink) = CreateLogger();
        logger.Write(LogEventLevel.Information, "{" + name + "}", "nilai-rahasia");

        Rendered(sink).Should().NotContain("nilai-rahasia");
    }

    [Fact]
    public void Kalimat_biasa_dengan_kata_bearer_tidak_diubah()
    {
        var (logger, sink) = CreateLogger();

        logger.Information("Detail {Detail}", "bearer authentication failed; basic credentials missing");

        Rendered(sink).Should().Contain("bearer authentication failed; basic credentials missing");
    }

    [Fact]
    public void Properti_biasa_tidak_diubah()
    {
        var (logger, sink) = CreateLogger();

        logger.Information("Tugas {TaskId} diubah oleh {UserId}", 42, "u-1");

        Rendered(sink).Should().Be("Tugas 42 diubah oleh \"u-1\"");
    }
}
