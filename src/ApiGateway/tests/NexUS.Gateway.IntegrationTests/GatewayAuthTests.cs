using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;

namespace NexUS.Gateway.IntegrationTests;

public class GatewayAuthTests(GatewayFixture fixture) : IClassFixture<GatewayFixture>
{
    [Fact]
    public async Task Request_tanpa_token_ke_projects_401_di_Gateway_dan_tidak_sampai_ke_service()
    {
        var before = fixture.Received.Count;

        var response = await fixture.CreateClient(authenticated: false).GetAsync("/api/v1/projects");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        fixture.Received.Count.Should().Be(before);
    }

    [Theory]
    [InlineData("/api/v1/projects")]
    [InlineData("/api/v1/tasks/1")]
    [InlineData("/api/v1/users/me")]
    [InlineData("/api/v1/sops")]
    public async Task Request_dengan_token_sah_diteruskan(string path)
    {
        (await fixture.CreateClient().GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/api/v1/auth/login")]
    [InlineData("/api/v1/projects/health")]
    [InlineData("/health/live")]
    public async Task Endpoint_publik_tidak_butuh_token(string path)
    {
        (await fixture.CreateClient(authenticated: false).GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Setelah_logout_access_token_lama_ditolak_401_walau_belum_kedaluwarsa()
    {
        var token = fixture.Tokens.Create(sid: "sesi-logout-gateway");
        var client = fixture.CreateClient(authenticated: false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await client.GetAsync("/api/v1/projects")).StatusCode.Should().Be(HttpStatusCode.OK);

        await fixture.Revocations.RevokeSessionAsync(1, "sesi-logout-gateway"); // yang ditulis Auth saat logout

        (await client.GetAsync("/api/v1/projects")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_dengan_signature_lain_ditolak()
    {
        using var other = new TestTokens();
        var client = fixture.CreateClient(authenticated: false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", other.Create());

        (await client.GetAsync("/api/v1/projects")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Hub_tanpa_token_401_dan_dengan_access_token_di_query_diteruskan()
    {
        (await fixture.CreateClient(authenticated: false).PostAsync("/hubs/notifications/negotiate", null))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await fixture.CreateClient(authenticated: false)
                .PostAsync($"/hubs/notifications/negotiate?access_token={fixture.Tokens.Create()}", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
