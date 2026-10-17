using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexUS.BuildingBlocks.Observability;

namespace NexUS.BuildingBlocks.Tests.Observability;

public class CorrelationIdTests
{
    private sealed class RecordingHandler : DelegatingHandler
    {
        public string? ReceivedCorrelationId { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ReceivedCorrelationId = request.Headers.TryGetValues(CorrelationId.HeaderName, out var values) ? values.Single() : null;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }

    private static async Task<(IHost Host, RecordingHandler Downstream)> StartAsync()
    {
        var downstream = new RecordingHandler();
        var host = new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddHttpClient("downstream", c => c.BaseAddress = new Uri("http://downstream"))
                        .AddCorrelationIdPropagation()
                        .ConfigurePrimaryHttpMessageHandler(() => downstream);
                })
                .Configure(app =>
                {
                    app.UseMiddleware<CorrelationIdMiddleware>();
                    app.UseRouting();
                    app.UseEndpoints(e =>
                    {
                        e.MapGet("/ping", () => "pong");
                        e.MapGet("/call", async (IHttpClientFactory factory) =>
                        {
                            await factory.CreateClient("downstream").GetAsync("/x");
                            return "ok";
                        });
                    });
                }))
            .Build();
        await host.StartAsync();
        return (host, downstream);
    }

    [Fact]
    public async Task Request_tanpa_header_mendapat_correlation_id_baru_di_response()
    {
        var (host, _) = await StartAsync();
        using var _h = host;

        var response = await host.GetTestClient().GetAsync("/ping");

        response.Headers.GetValues(CorrelationId.HeaderName).Single().Should().HaveLength(32);
    }

    [Theory]
    [InlineData("abc-123")]
    [InlineData("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01")]
    [InlineData("gateway.trace:42")]
    public async Task Header_yang_valid_dipakai_ulang_dan_dikembalikan(string id)
    {
        var (host, _) = await StartAsync();
        using var _h = host;
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(CorrelationId.HeaderName, id);

        var response = await client.GetAsync("/ping");

        response.Headers.GetValues(CorrelationId.HeaderName).Single().Should().Be(id);
    }

    [Theory]
    [InlineData("a b")]
    [InlineData("<script>")]
    [InlineData("")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task Header_yang_tidak_aman_diganti_dengan_id_baru(string unsafeValue)
    {
        var (host, _) = await StartAsync();
        using var _h = host;
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(CorrelationId.HeaderName, unsafeValue);

        var response = await client.GetAsync("/ping");

        response.Headers.GetValues(CorrelationId.HeaderName).Single().Should().NotBe(unsafeValue).And.HaveLength(32);
    }

    [Fact]
    public async Task Correlation_id_diteruskan_ke_panggilan_HttpClient_antar_service()
    {
        var (host, downstream) = await StartAsync();
        using var _h = host;
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(CorrelationId.HeaderName, "trace-777");

        await client.GetAsync("/call");

        downstream.ReceivedCorrelationId.Should().Be("trace-777");
    }
}
