using NexUS.BuildingBlocks.Observability;
using NexUS.Audit.Api.Middleware;
using NexUS.Audit.Application;
using NexUS.Audit.Infrastructure;

if (HealthProbe.IsRequested(args)) return await HealthProbe.RunAsync();

var builder = WebApplication.CreateBuilder(args);

builder.Host.AddNexusSerilog();

builder.Services
    .AddAuditApplication()
    .AddAuditInfrastructure();

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddNexusHealthChecks(builder.Configuration);

var app = builder.Build();

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
