using System.Net;
using FluentAssertions;

namespace NexUS.Gateway.IntegrationTests;

public class GatewayRoutingTests(GatewayFixture fixture) : IClassFixture<GatewayFixture>
{
    private ReceivedRequest LastReceived() => fixture.Received.Last();

    [Fact]
    public async Task Health_project_lewat_Gateway_diteruskan_ke_health_ready_Project_Service()
    {
        var response = await fixture.CreateClient().GetAsync("/api/v1/projects/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        LastReceived().Path.Should().Be("/health/ready");
    }

    [Theory]
    [InlineData("/api/v1/auth/login")]
    [InlineData("/api/v1/users/42")]
    [InlineData("/api/v1/roles")]
    [InlineData("/api/v1/projects")]
    [InlineData("/api/v1/projects/abc/members")]
    [InlineData("/api/v1/tasks/1")]
    [InlineData("/api/v1/sops/1/versions")]
    [InlineData("/api/v1/notifications")]
    public async Task Route_terdaftar_diteruskan_dengan_path_utuh(string path)
    {
        var response = await fixture.CreateClient().GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        LastReceived().Path.Should().Be(path);
    }

    [Theory]
    [InlineData("/api/v1/tidak-ada")]
    [InlineData("/api/v2/projects")]
    [InlineData("/api/projects")]
    [InlineData("/")]
    public async Task Route_tidak_terdaftar_mengembalikan_404_ProblemDetails(string path)
    {
        var response = await fixture.CreateClient().GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Correlation_id_dari_client_diteruskan_ke_service_dan_dikembalikan()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "trace-gw-001");

        var response = await client.GetAsync("/api/v1/tasks/7");

        response.Headers.GetValues("X-Correlation-Id").Single().Should().Be("trace-gw-001");
        LastReceived().CorrelationId.Should().Be("trace-gw-001");
    }

    [Fact]
    public async Task Correlation_id_yang_dibuat_Gateway_ikut_dikirim_ke_service()
    {
        var response = await fixture.CreateClient().GetAsync("/api/v1/sops/9");

        var generated = response.Headers.GetValues("X-Correlation-Id").Single();
        generated.Should().HaveLength(32);
        LastReceived().CorrelationId.Should().Be(generated);
    }

    [Fact]
    public async Task Respons_memuat_header_keamanan_tanpa_header_Server()
    {
        var response = await fixture.CreateClient().GetAsync("/api/v1/projects");

        response.Headers.GetValues("X-Content-Type-Options").Single().Should().Be("nosniff");
        response.Headers.GetValues("X-Frame-Options").Single().Should().Be("DENY");
        response.Headers.GetValues("Referrer-Policy").Single().Should().Be("no-referrer");
        response.Headers.Contains("Server").Should().BeFalse();
    }
}
