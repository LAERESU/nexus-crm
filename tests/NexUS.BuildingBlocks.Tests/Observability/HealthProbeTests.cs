using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using NexUS.BuildingBlocks.Observability;

namespace NexUS.BuildingBlocks.Tests.Observability;

public class HealthProbeTests
{
    [Fact]
    public void IsRequested_true_hanya_bila_flag_ada()
    {
        HealthProbe.IsRequested(["--healthcheck"]).Should().BeTrue();
        HealthProbe.IsRequested(["--urls", "x"]).Should().BeFalse();
        HealthProbe.IsRequested([]).Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_mengembalikan_0_bila_endpoint_200()
    {
        using var server = StartServer(HttpStatusCode.OK, out var url);

        (await HealthProbe.RunAsync(url)).Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_mengembalikan_1_bila_endpoint_error()
    {
        using var server = StartServer(HttpStatusCode.ServiceUnavailable, out var url);

        (await HealthProbe.RunAsync(url)).Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_mengembalikan_1_bila_tidak_ada_server()
    {
        var port = FreePort();

        (await HealthProbe.RunAsync($"http://localhost:{port}/health/live")).Should().Be(1);
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static HttpListener StartServer(HttpStatusCode status, out string url)
    {
        var port = FreePort();
        url = $"http://localhost:{port}/health/live";
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        _ = Task.Run(async () =>
        {
            try
            {
                var context = await listener.GetContextAsync();
                context.Response.StatusCode = (int)status;
                context.Response.Close();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { }
        });
        return listener;
    }
}
