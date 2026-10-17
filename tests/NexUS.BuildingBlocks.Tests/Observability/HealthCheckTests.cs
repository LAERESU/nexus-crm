using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexUS.BuildingBlocks.Observability;

namespace NexUS.BuildingBlocks.Tests.Observability;

public class HealthCheckTests
{
    // Port 1 pada loopback tidak pernah listen, sehingga koneksi langsung ditolak.
    private const string Oracle = "User Id=X;Password=y;Data Source=127.0.0.1:1/FREEPDB1";
    private const string Redis = "127.0.0.1:1,abortConnect=false";
    private const string RabbitMq = "amqp://u:p@127.0.0.1:1";

    private static async Task<IHost> StartAsync(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var host = new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services => services.AddRouting().AddNexusHealthChecks(configuration))
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e =>
                    {
                        e.MapHealthChecks("/health/live", new() { Predicate = _ => false });
                        e.MapHealthChecks("/health/ready", new() { Predicate = c => c.Tags.Contains(HealthCheckExtensions.ReadyTag) });
                    });
                }))
            .Build();
        await host.StartAsync();
        return host;
    }

    [Fact]
    public async Task Ready_200_bila_tidak_ada_dependency_yang_dikonfigurasi()
    {
        using var host = await StartAsync([]);

        var response = await host.GetTestClient().GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_503_bila_Oracle_mati()
    {
        using var host = await StartAsync(new() { ["ConnectionStrings:Oracle"] = Oracle });

        var response = await host.GetTestClient().GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    // Redis hanya cache dan RabbitMQ memakai Outbox: kegagalannya Degraded, service tetap menerima trafik.
    [Theory]
    [InlineData("ConnectionStrings:Redis", Redis)]
    [InlineData("ConnectionStrings:RabbitMq", RabbitMq)]
    public async Task Ready_tetap_200_tetapi_Degraded_bila_Redis_atau_RabbitMQ_mati(string key, string connectionString)
    {
        using var host = await StartAsync(new() { [key] = connectionString });

        var response = await host.GetTestClient().GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Degraded");
    }

    [Fact]
    public async Task Respons_tidak_membocorkan_connection_string()
    {
        using var host = await StartAsync(new() { ["ConnectionStrings:Oracle"] = Oracle });

        var body = await (await host.GetTestClient().GetAsync("/health/ready")).Content.ReadAsStringAsync();

        body.Should().NotContain("Password").And.NotContain("127.0.0.1");
    }

    [Fact]
    public async Task Live_tetap_200_walau_semua_dependency_mati()
    {
        using var host = await StartAsync(new()
        {
            ["ConnectionStrings:Oracle"] = Oracle,
            ["ConnectionStrings:Redis"] = Redis,
            ["ConnectionStrings:RabbitMq"] = RabbitMq,
        });

        var live = await host.GetTestClient().GetAsync("/health/live");
        var ready = await host.GetTestClient().GetAsync("/health/ready");

        live.StatusCode.Should().Be(HttpStatusCode.OK);
        ready.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}
