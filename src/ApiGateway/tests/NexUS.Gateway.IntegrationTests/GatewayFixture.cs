using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace NexUS.Gateway.IntegrationTests;

/// <summary>Request yang diterima service tiruan di belakang Gateway.</summary>
public sealed record ReceivedRequest(string Path, string? CorrelationId);

/// <summary>Gateway sungguhan (WebApplicationFactory) yang meneruskan ke satu service tiruan berbasis Kestrel.</summary>
public sealed class GatewayFixture : IAsyncLifetime
{
    public const string AllowedOrigin = "http://localhost:5173";
    private static readonly string[] Clusters = ["auth", "user", "project", "task", "sop", "notification"];

    private WebApplication? _downstream;
    private WebApplicationFactory<Program>? _gateway;

    public ConcurrentQueue<ReceivedRequest> Received { get; } = new();

    public HttpClient CreateClient() => _gateway!.CreateClient();

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _downstream = builder.Build();
        _downstream.Run(async context =>
        {
            Received.Enqueue(new ReceivedRequest(context.Request.Path, context.Request.Headers["X-Correlation-Id"].FirstOrDefault()));
            await context.Response.WriteAsJsonAsync(new { path = context.Request.Path.Value });
        });
        await _downstream.StartAsync();
        var downstreamUrl = _downstream.Urls.Single();

        var settings = Clusters.ToDictionary(
            c => $"ReverseProxy:Clusters:{c}:Destinations:d1:Address",
            _ => (string?)downstreamUrl);
        settings["Cors:AllowedOrigins:0"] = AllowedOrigin;

        _gateway = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Testing")
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings)));
    }

    public async Task DisposeAsync()
    {
        if (_gateway is not null) await _gateway.DisposeAsync();
        if (_downstream is not null) await _downstream.DisposeAsync();
    }
}
