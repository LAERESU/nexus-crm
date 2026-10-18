using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexUS.BuildingBlocks.Auth;
using NexUS.BuildingBlocks.Common.Domain;
using NexUS.Testing;

namespace NexUS.BuildingBlocks.Tests.Auth;

/// <summary>
/// Pipeline autentikasi yang sama dengan yang dipasang setiap service (<c>AddNexusJwtAuth</c> + fallback policy),
/// dipanggil langsung tanpa Gateway. JWKS disajikan dari server tes sendiri.
/// </summary>
public sealed class JwtValidationTests : IAsyncLifetime
{
    private readonly TestTokens _tokens = new();
    private readonly InMemoryTokenRevocationStore _revocations = new();
    private IHost _host = default!;

    public async Task InitializeAsync()
    {
        _host = new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .UseEnvironment(Environments.Development)
                .ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:Jwt:JwksUrl"] = "http://jwks.test/.well-known/jwks.json",
                    ["Auth:Jwt:RequireHttpsMetadata"] = "false",
                }))
                .ConfigureServices((context, services) =>
                {
                    services.AddRouting();
                    services.AddNexusJwtAuth(context.Configuration);
                    services.AddSingleton<ITokenRevocationStore>(_revocations);
                    services.AddHttpClient(ServiceCollectionExtensions.JwksHttpClient)
                        .ConfigurePrimaryHttpMessageHandler(() => new StaticJwksHandler(_tokens.Jwks));
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(e =>
                    {
                        e.MapGet("/api/v1/projects", (ICurrentUser user) =>
                            Results.Ok(new { user.UserId, user.TenantId, user.SessionId, user.Roles }));
                        e.MapGet("/health/live", () => "ok").AllowAnonymous();
                        e.MapGet("/hubs/notifications/negotiate", () => "hub");
                    });
                }))
            .Build();
        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
        _tokens.Dispose();
    }

    private Task<HttpResponseMessage> Get(string path, string? token)
    {
        var client = _host.GetTestClient();
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.GetAsync(path);
    }

    [Fact]
    public async Task Token_valid_diterima_dan_ICurrentUser_terisi_dari_klaim()
    {
        var userId = Guid.NewGuid();
        var response = await Get("/api/v1/projects", _tokens.Create(tenantId: 9, userId: userId, sid: "sesi-x", roles: ["pm"]));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(userId.ToString()).And.Contain("\"tenantId\":9").And.Contain("sesi-x").And.Contain("pm");
    }

    [Fact]
    public async Task Tanpa_token_401()
    {
        (await Get("/api/v1/projects", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Signature_salah_401()
    {
        using var other = new TestTokens();

        (await Get("/api/v1/projects", other.Create())).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Audience_salah_401()
    {
        (await Get("/api/v1/projects", _tokens.Create(audience: "aplikasi-lain"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Issuer_salah_401()
    {
        (await Get("/api/v1/projects", _tokens.Create(issuer: "penerbit-lain"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_kedaluwarsa_melewati_clock_skew_401()
    {
        var token = _tokens.Create(expires: DateTimeOffset.UtcNow - NexusJwt.ClockSkew - TimeSpan.FromSeconds(5));

        (await Get("/api/v1/projects", token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_yang_jti_nya_dicabut_401()
    {
        var token = _tokens.Create(jti: "jti-dicabut");
        await _revocations.RevokeTokenAsync(1, "jti-dicabut", DateTimeOffset.UtcNow.AddMinutes(15), DateTimeOffset.UtcNow);

        (await Get("/api/v1/projects", token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_dari_sesi_yang_dicabut_401()
    {
        var token = _tokens.Create(sid: "sesi-logout");
        await _revocations.RevokeSessionAsync(1, "sesi-logout");

        (await Get("/api/v1/projects", token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Pencabutan_hanya_berlaku_di_tenant_yang_sama()
    {
        await _revocations.RevokeSessionAsync(2, "sesi-1");

        (await Get("/api/v1/projects", _tokens.Create(tenantId: 1, sid: "sesi-1"))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_tetap_anonim()
    {
        (await Get("/health/live", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Hub_menerima_token_dari_query_string_access_token()
    {
        var response = await _host.GetTestClient().GetAsync($"/hubs/notifications/negotiate?access_token={_tokens.Create()}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Token_di_query_string_diabaikan_di_luar_hub()
    {
        var response = await _host.GetTestClient().GetAsync($"/api/v1/projects?access_token={_tokens.Create()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Di_luar_Development_tanpa_JwksUrl_aplikasi_gagal_start()
    {
        using var host = new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .UseEnvironment(Environments.Production)
                .ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Redis"] = "localhost:1,abortConnect=false",
                }))
                .ConfigureServices((context, services) => services.AddRouting().AddNexusJwtAuth(context.Configuration))
                .Configure(app => app.UseRouting()))
            .Build();

        var act = () => host.StartAsync();

        (await act.Should().ThrowAsync<Microsoft.Extensions.Options.OptionsValidationException>())
            .Which.Message.Should().Contain("Auth:Jwt:JwksUrl");
    }

    private sealed class StaticJwksHandler(string jwks) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(jwks) });
    }
}
