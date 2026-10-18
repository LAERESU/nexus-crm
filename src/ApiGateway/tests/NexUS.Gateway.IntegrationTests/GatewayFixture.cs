using System.Collections.Concurrent;
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

/// <summary>Request yang diterima service tiruan di belakang Gateway.</summary>
public sealed record ReceivedRequest(string Path, string? CorrelationId);

/// <summary>Gateway sungguhan (WebApplicationFactory) yang meneruskan ke satu service tiruan berbasis Kestrel.</summary>
public sealed class GatewayFixture : IAsyncLifetime
{
    public const string AllowedOrigin = "http://localhost:5173";
    private static readonly string[] Clusters = ["auth", "user", "project", "task", "sop", "notification", "notification-hub"];

    private WebApplication? _downstream;
    private WebApplicationFactory<Program>? _gateway;

    public ConcurrentQueue<ReceivedRequest> Received { get; } = new();
    public TestTokens Tokens { get; } = new();
    public InMemoryTokenRevocationStore Revocations { get; } = new();

    /// <summary>Klien dengan access token sah (default) atau tanpa token.</summary>
    public HttpClient CreateClient(bool authenticated = true)
    {
        var client = _gateway!.CreateClient();
        if (authenticated)
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Tokens.Create());
        return client;
    }

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _downstream = builder.Build();
        _downstream.Run(async context =>
        {
            if (context.Request.Path == "/.well-known/jwks.json")
            {
                await context.Response.WriteAsync(Tokens.Jwks); // JWKS "Auth Service" tiruan
                return;
            }

            Received.Enqueue(new ReceivedRequest(context.Request.Path, context.Request.Headers["X-Correlation-Id"].FirstOrDefault()));
            await context.Response.WriteAsJsonAsync(new { path = context.Request.Path.Value });
        });
        await _downstream.StartAsync();
        var downstreamUrl = _downstream.Urls.Single();

        var settings = Clusters.ToDictionary(
            c => $"ReverseProxy:Clusters:{c}:Destinations:d1:Address",
            _ => (string?)downstreamUrl);
        settings["Cors:AllowedOrigins:0"] = AllowedOrigin;
        settings["Auth:Jwt:JwksUrl"] = $"{downstreamUrl}/.well-known/jwks.json";
        settings["Auth:Jwt:RequireHttpsMetadata"] = "false";

        _gateway = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Development") // tanpa Redis: cache, blacklist, dan rate limit lolos (lihat GatewayRateLimitFixture untuk Redis)
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings))
            .ConfigureTestServices(services => services.AddSingleton<ITokenRevocationStore>(Revocations)));
    }

    public async Task DisposeAsync()
    {
        if (_gateway is not null) await _gateway.DisposeAsync();
        if (_downstream is not null) await _downstream.DisposeAsync();
        Tokens.Dispose();
    }
}
