using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexUS.BuildingBlocks.Auth;
using NexUS.Testing;

namespace NexUS.Gateway.IntegrationTests;

/// <summary>Dua instance Gateway berbagi satu Redis sungguhan (Testcontainers) di depan satu service tiruan.</summary>
public sealed class GatewayRateLimitFixture : IAsyncLifetime
{
    private readonly IContainer _redis = new ContainerBuilder("redis:7-alpine")
        .WithPortBinding(6379, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Ready to accept connections"))
        .Build();

    private WebApplication? _downstream;

    public TestTokens Tokens { get; } = new();
    public WebApplicationFactory<Program> GatewayA { get; private set; } = default!;
    public WebApplicationFactory<Program> GatewayB { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        await _redis.StartAsync();

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _downstream = builder.Build();
        _downstream.Run(context => context.Request.Path == "/.well-known/jwks.json"
            ? context.Response.WriteAsync(Tokens.Jwks)
            : context.Response.WriteAsync("ok"));
        await _downstream.StartAsync();
        var downstreamUrl = _downstream.Urls.Single();

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Redis"] = $"{_redis.Hostname}:{_redis.GetMappedPublicPort(6379)},abortConnect=false",
            ["Auth:Jwt:JwksUrl"] = $"{downstreamUrl}/.well-known/jwks.json",
            ["Auth:Jwt:RequireHttpsMetadata"] = "false",
            ["Cors:AllowedOrigins:0"] = "http://localhost:5173",
            // Batas kecil untuk login/upload/tenant agar tes cepat; per-user memakai batas asli (300/menit).
            ["RateLimiting:Login:PermitLimit"] = "3",
            ["RateLimiting:Upload:PermitLimit"] = "2",
            ["RateLimiting:PerTenant:PermitLimit"] = "320",
        };
        foreach (var cluster in new[] { "auth", "user", "project", "task", "sop", "notification", "notification-hub" })
            settings[$"ReverseProxy:Clusters:{cluster}:Destinations:d1:Address"] = downstreamUrl;

        GatewayA = CreateGateway(settings);
        GatewayB = CreateGateway(settings);
    }

    private static WebApplicationFactory<Program> CreateGateway(Dictionary<string, string?> settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Testing")
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings))
            .ConfigureTestServices(services => services.AddSingleton<ITokenRevocationStore>(new InMemoryTokenRevocationStore())));

    public HttpClient Client(WebApplicationFactory<Program> gateway, string? token)
    {
        var client = gateway.CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task DisposeAsync()
    {
        await GatewayA.DisposeAsync();
        await GatewayB.DisposeAsync();
        if (_downstream is not null) await _downstream.DisposeAsync();
        await _redis.DisposeAsync();
        Tokens.Dispose();
    }
}

[Trait("Category", "Docker")]
public class GatewayRateLimitTests(GatewayRateLimitFixture fixture) : IClassFixture<GatewayRateLimitFixture>
{
    [Fact]
    public async Task Request_ke_301_dalam_satu_menit_oleh_user_yang_sama_429_dengan_Retry_After()
    {
        var client = fixture.Client(fixture.GatewayA, fixture.Tokens.Create(tenantId: 101, userId: Guid.NewGuid()));

        for (var i = 1; i <= 300; i++)
            (await client.GetAsync("/api/v1/projects")).StatusCode.Should().Be(HttpStatusCode.OK, $"request ke-{i} masih dalam batas");

        var rejected = await client.GetAsync("/api/v1/projects");

        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter!.Delta!.Value.Should().BeGreaterThan(TimeSpan.Zero).And.BeLessThanOrEqualTo(TimeSpan.FromMinutes(1));
        rejected.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Dua_instance_Gateway_berbagi_hitungan_yang_sama()
    {
        var token = fixture.Tokens.Create(tenantId: 102, userId: Guid.NewGuid());
        var a = fixture.Client(fixture.GatewayA, token);
        var b = fixture.Client(fixture.GatewayB, token);

        for (var i = 1; i <= 300; i++)
            (await (i % 2 == 0 ? a : b).GetAsync("/api/v1/projects")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await a.GetAsync("/api/v1/projects")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await b.GetAsync("/api/v1/projects")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task User_lain_tidak_terkena_batas_user_pertama()
    {
        var first = fixture.Client(fixture.GatewayA, fixture.Tokens.Create(tenantId: 103, userId: Guid.NewGuid()));
        for (var i = 0; i < 301; i++) await first.GetAsync("/api/v1/projects");

        var other = fixture.Client(fixture.GatewayA, fixture.Tokens.Create(tenantId: 103, userId: Guid.NewGuid()));

        (await other.GetAsync("/api/v1/projects")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_dibatasi_per_IP_dan_email_dan_tetap_meneruskan_body_ke_Auth()
    {
        var client = fixture.Client(fixture.GatewayA, null);
        var email = $"{Guid.NewGuid():N}@demo.local";

        for (var i = 0; i < 3; i++)
            (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "x" })).StatusCode.Should().Be(HttpStatusCode.OK);

        var rejected = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "x" });
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter!.Delta!.Value.Should().BeGreaterThan(TimeSpan.Zero).And.BeLessThanOrEqualTo(TimeSpan.FromMinutes(15));

        (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "lain@demo.local", password = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.OK, "email lain punya hitungan sendiri");
    }

    [Theory]
    [InlineData("/api/v1/auth/login/")]
    [InlineData("/API/V1/AUTH/LOGIN")]
    public async Task Varian_path_login_tetap_terkena_batas_login(string path)
    {
        var client = fixture.Client(fixture.GatewayA, null);
        var email = $"{Guid.NewGuid():N}@demo.local";

        for (var i = 0; i < 3; i++) await client.PostAsJsonAsync(path, new { email, password = "x" });

        (await client.PostAsJsonAsync(path, new { email, password = "x" })).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Email_ganda_atau_beda_huruf_besar_kecil_dibaca_sama_seperti_Auth()
    {
        var client = fixture.Client(fixture.GatewayA, null);
        var victim = $"{Guid.NewGuid():N}@demo.local";
        HttpContent Body(int n) => new StringContent(
            $"{{\"email\":\"acak-{n}@x.local\",\"Email\":\"{victim}\",\"password\":\"x\"}}",
            System.Text.Encoding.UTF8, "application/json");

        for (var i = 0; i < 3; i++) await client.PostAsync("/api/v1/auth/login", Body(i));

        (await client.PostAsync("/api/v1/auth/login", Body(99))).StatusCode
            .Should().Be(HttpStatusCode.TooManyRequests, "kunci memakai email terakhir tanpa peduli huruf besar-kecil, sama seperti Auth");
    }

    [Fact]
    public async Task User_yang_sudah_ditolak_tidak_menguras_kuota_tenant()
    {
        // Batas tenant di fixture: 320/menit. User A memakai 300 lalu terus ditolak; user B di tenant yang sama tetap bisa.
        var tenant = 105;
        var a = fixture.Client(fixture.GatewayA, fixture.Tokens.Create(tenantId: tenant, userId: Guid.NewGuid()));
        for (var i = 0; i < 400; i++) await a.GetAsync("/api/v1/projects");

        var b = fixture.Client(fixture.GatewayA, fixture.Tokens.Create(tenantId: tenant, userId: Guid.NewGuid()));

        (await b.GetAsync("/api/v1/projects")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Upload_multipart_dibatasi_terpisah_dari_request_biasa()
    {
        var client = fixture.Client(fixture.GatewayA, fixture.Tokens.Create(tenantId: 104, userId: Guid.NewGuid()));
        HttpContent Upload() => new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "a.bin" } };

        (await client.PostAsync("/api/v1/tasks/1/attachments", Upload())).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync("/api/v1/tasks/1/attachments", Upload())).StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.PostAsync("/api/v1/tasks/1/attachments", Upload())).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await client.GetAsync("/api/v1/tasks/1")).StatusCode.Should().Be(HttpStatusCode.OK, "request biasa tidak terkena batas upload");
    }
}

/// <summary>Kelas terpisah (fixture dan Redis sendiri): menguras kuota IP bersama tidak boleh memengaruhi tes lain.</summary>
[Trait("Category", "Docker")]
public class GatewayAnonymousRateLimitTests(GatewayRateLimitFixture fixture) : IClassFixture<GatewayRateLimitFixture>
{
    [Fact]
    public async Task Request_anonim_dibatasi_per_IP()
    {
        // Semua request tes berasal dari IP yang sama; batas per-IP 300/menit.
        var client = fixture.Client(fixture.GatewayB, null);
        HttpStatusCode last = HttpStatusCode.OK;
        for (var i = 0; i < 400 && last != HttpStatusCode.TooManyRequests; i++)
            last = (await client.PostAsync("/api/v1/auth/refresh", null)).StatusCode;

        last.Should().Be(HttpStatusCode.TooManyRequests);
    }

}
