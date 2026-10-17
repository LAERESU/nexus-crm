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

    [Theory]
    [InlineData("ConnectionStrings:Oracle", Oracle)]
    [InlineData("ConnectionStrings:Redis", Redis)]
    [InlineData("ConnectionStrings:RabbitMq", RabbitMq)]
    public async Task Ready_503_bila_dependency_mati(string key, string connectionString)
    {
        using var host = await StartAsync(new() { [key] = connectionString });

        var response = await host.GetTestClient().GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
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
