using NexUS.BuildingBlocks.Auth;
using NexUS.BuildingBlocks.Observability;
using NexUS.Tasks.Api.Middleware;
using NexUS.Tasks.Application;
using NexUS.Tasks.Infrastructure;

if (HealthProbe.IsRequested(args)) return await HealthProbe.RunAsync();

var builder = WebApplication.CreateBuilder(args);

builder.Host.AddNexusSerilog();

builder.Services
    .AddTaskApplication()
    .AddTaskInfrastructure();

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddNexusHealthChecks(builder.Configuration);
builder.Services.AddNexusJwtAuth(builder.Configuration);

var app = builder.Build();

app.UseNexusObservability();
app.UseMiddleware<ExceptionHandlingMiddleware>(); // juga menangkap error saat autentikasi (mis. Redis salah konfigurasi)
// Swagger sebelum autentikasi: fallback policy juga berlaku untuk request tanpa endpoint (middleware Swagger).
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication(); // TenantId/UserId di log diisi HttpContextUserEnricher saat log ditulis
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

app.Run();
return 0;

public partial class Program;
