using Microsoft.AspNetCore.HttpOverrides;
using NexUS.BuildingBlocks.Observability;
using NexUS.Auth.Api.Middleware;
using NexUS.Auth.Application;
using NexUS.Auth.Infrastructure;

if (HealthProbe.IsRequested(args)) return await HealthProbe.RunAsync();

var builder = WebApplication.CreateBuilder(args);

builder.Host.AddNexusSerilog();

builder.Services
    .AddAuthApplication()
    .AddAuthInfrastructure(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddNexusHealthChecks(builder.Configuration);

// Auth hanya dapat dijangkau lewat Gateway (jaringan internal), jadi X-Forwarded-For dari hop terakhir dipercaya
// agar LOGIN_HISTORY dan REFRESH_TOKENS mencatat IP klien asli, bukan IP Gateway.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

app.UseNexusObservability();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseNexusLogEnrichment();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") });

app.Run();
return 0;

public partial class Program;
