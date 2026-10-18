using Microsoft.AspNetCore.Cors.Infrastructure;
using NexUS.BuildingBlocks.Auth;
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
        // X-Requested-With dan X-SignalR-User-Agent dikirim klien SignalR saat negotiate ke /hubs/notifications.
        .WithHeaders("Authorization", "Content-Type", "If-Match", CorrelationId.HeaderName, "X-Requested-With", "X-SignalR-User-Agent")
        .WithExposedHeaders(CorrelationId.HeaderName, "ETag", "Location")));

builder.Services.AddProblemDetails();
builder.Services.AddNexusHealthChecks(builder.Configuration);
// Setiap route YARP menetapkan AuthorizationPolicy sendiri ("default" = wajib token, "anonymous" = publik).
// Tanpa fallback policy, path yang tidak terdaftar tetap 404 (NX-011).
builder.Services.AddNexusJwtAuth(builder.Configuration, requireAuthenticationByDefault: false);

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
app.UseExceptionHandler(); // exception di pipeline Gateway → 500 ProblemDetails tanpa stack trace
app.UseMiddleware<RejectDisallowedOriginMiddleware>();
app.UseCors(GatewayCors.PolicyName);
app.UseStatusCodePages(); // route tidak terdaftar → 404 ProblemDetails
app.UseAuthentication();
app.UseNexusLogEnrichment();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains(HealthCheckExtensions.ReadyTag) }).AllowAnonymous();

// Endpoint internal antar service tidak boleh diekspos lewat Gateway (ARCHITECTURE.md: hanya untuk Auth/Gateway).
// Path literal lebih diprioritaskan daripada route catch-all YARP.
foreach (var internalPath in GatewayRoutes.InternalPaths)
    app.Map(internalPath, () => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found")).AllowAnonymous();

app.MapReverseProxy();

app.Run();
return 0;

public partial class Program;
