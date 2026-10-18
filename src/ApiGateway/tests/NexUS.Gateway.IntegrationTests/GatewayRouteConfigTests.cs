using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NexUS.Gateway.Api;

namespace NexUS.Gateway.IntegrationTests;

public class GatewayRouteConfigTests
{
    private static IConfiguration Config(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    [Fact]
    public void Route_tanpa_AuthorizationPolicy_ditolak_saat_start()
    {
        var configuration = Config(new()
        {
            ["ReverseProxy:Routes:projects:ClusterId"] = "project",
            ["ReverseProxy:Routes:projects:AuthorizationPolicy"] = "default",
            ["ReverseProxy:Routes:reports:ClusterId"] = "report", // lupa policy
        });

        var act = () => GatewayRoutes.EnsureAuthorizationPolicies(configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage("*reports*");
    }

    [Fact]
    public void Konfigurasi_Gateway_yang_dikirim_semua_route_punya_policy()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var configuration = new ConfigurationBuilder().AddJsonFile(path).Build();

        var act = () => GatewayRoutes.EnsureAuthorizationPolicies(configuration);

        act.Should().NotThrow();
    }
}
