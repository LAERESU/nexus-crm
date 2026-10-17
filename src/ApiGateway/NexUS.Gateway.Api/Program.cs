using Microsoft.AspNetCore.Cors.Infrastructure;
using NexUS.BuildingBlocks.Observability;
using NexUS.Gateway.Api;
using NexUS.Gateway.Api.Middleware;
using Yarp.ReverseProxy.Transforms;

if (HealthProbe.IsRequested(args)) return await HealthProbe.RunAsync();

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);
builder.Host.AddNexusSerilog();

builder.Services.AddCors();
builder.Services.AddOptions<CorsOptions>().Configure<IConfiguration>((options, configuration) =>
    options.AddPolicy(GatewayCors.PolicyName, policy => policy
        .WithOrigins(GatewayCors.AllowedOrigins(configuration))
        .AllowCredentials() // refresh token dikirim lewat cookie httpOnly
        .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
        .WithHeaders("Authorization", "Content-Type", "If-Match", CorrelationId.HeaderName)
        .WithExposedHeaders(CorrelationId.HeaderName, "ETag", "Location")));

builder.Services.AddProblemDetails();
builder.Services.AddNexusHealthChecks(builder.Configuration);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(context => context.AddRequestTransform(transform =>
    {
        // Correlation id yang dibuat Gateway ikut dikirim ke service tujuan agar log keduanya bisa dikorelasikan.
        if (transform.HttpContext.Items[CorrelationId.ItemKey] is string correlationId)
        {
            transform.ProxyRequest.Headers.Remove(CorrelationId.HeaderName);
            transform.ProxyRequest.Headers.TryAddWithoutValidation(CorrelationId.HeaderName, correlationId);
        }

        return ValueTask.CompletedTask;
    }));

var app = builder.Build();

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseNexusObservability();
app.UseMiddleware<RejectDisallowedOriginMiddleware>();
app.UseCors(GatewayCors.PolicyName);
app.UseStatusCodePages(); // route tidak terdaftar → 404 ProblemDetails

app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains(HealthCheckExtensions.ReadyTag) });
app.MapReverseProxy();

app.Run();
return 0;

public partial class Program;
