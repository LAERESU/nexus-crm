using NexUS.BuildingBlocks.Auth;
using NexUS.BuildingBlocks.Observability;
using NexUS.User.Api.Middleware;
using NexUS.User.Application;
using NexUS.User.Infrastructure;

if (HealthProbe.IsRequested(args)) return await HealthProbe.RunAsync();

var builder = WebApplication.CreateBuilder(args);

builder.Host.AddNexusSerilog();

builder.Services
    .AddUserApplication()
    .AddUserInfrastructure();

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddNexusHealthChecks(builder.Configuration);
builder.Services.AddNexusJwtAuth(builder.Configuration);

var app = builder.Build();

app.UseNexusObservability();
// Swagger sebelum autentikasi: fallback policy juga berlaku untuk request tanpa endpoint (middleware Swagger).
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseNexusLogEnrichment(); // setelah autentikasi agar TenantId/UserId terisi
app.UseMiddleware<ExceptionHandlingMiddleware>(); // di dalam scope enrichment: log error memuat tenant dan user
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

app.Run();
return 0;

public partial class Program;
