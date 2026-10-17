using System.Net;
using FluentAssertions;

namespace NexUS.Gateway.IntegrationTests;

public class GatewayCorsTests(GatewayFixture fixture) : IClassFixture<GatewayFixture>
{
    private static HttpRequestMessage Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/projects");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        return request;
    }

    [Fact]
    public async Task Preflight_dari_origin_frontend_diizinkan_dengan_credentials()
    {
        var response = await fixture.CreateClient().SendAsync(Preflight(GatewayFixture.AllowedOrigin));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").Single().Should().Be(GatewayFixture.AllowedOrigin);
        response.Headers.GetValues("Access-Control-Allow-Credentials").Single().Should().Be("true");
    }

    [Fact]
    public async Task Request_dari_origin_yang_diizinkan_diteruskan_dan_header_CORS_dikembalikan()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/projects");
        request.Headers.Add("Origin", GatewayFixture.AllowedOrigin);

        var response = await fixture.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("Access-Control-Allow-Origin").Single().Should().Be(GatewayFixture.AllowedOrigin);
        response.Headers.GetValues("Access-Control-Expose-Headers").Single().Should().Contain("X-Correlation-Id");
    }

    [Fact]
    public async Task Preflight_negotiate_SignalR_mengizinkan_header_klien_SignalR()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/hubs/notifications/negotiate");
        request.Headers.Add("Origin", GatewayFixture.AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "x-requested-with,x-signalr-user-agent");

        var response = await fixture.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var allowed = string.Join(",", response.Headers.GetValues("Access-Control-Allow-Headers")).ToLowerInvariant();
        allowed.Should().Contain("x-requested-with").And.Contain("x-signalr-user-agent");
    }

    [Fact]
    public async Task Origin_Gateway_sendiri_yang_tidak_terdaftar_tetap_ditolak()
    {
        var client = fixture.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/projects");
        request.Headers.Add("Origin", client.BaseAddress!.GetLeftPart(UriPartial.Authority));

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Preflight_dari_origin_tidak_dikenal_ditolak_403()
    {
        var response = await fixture.CreateClient().SendAsync(Preflight("https://evil.example"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task Request_dari_origin_tidak_dikenal_ditolak_dan_tidak_sampai_ke_service()
    {
        var before = fixture.Received.Count;
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/projects");
        request.Headers.Add("Origin", "https://evil.example");

        var response = await fixture.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        fixture.Received.Count.Should().Be(before);
    }

    [Fact]
    public async Task Request_tanpa_Origin_seperti_antar_service_tetap_diteruskan()
    {
        var response = await fixture.CreateClient().GetAsync("/api/v1/tasks");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
