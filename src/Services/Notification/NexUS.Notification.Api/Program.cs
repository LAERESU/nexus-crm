using NexUS.BuildingBlocks.Observability;
using NexUS.Notification.Api.Middleware;
using NexUS.Notification.Application;
using NexUS.Notification.Infrastructure;

if (HealthProbe.IsRequested(args)) return await HealthProbe.RunAsync();

var builder = WebApplication.CreateBuilder(args);

builder.Host.AddNexusSerilog();

builder.Services
    .AddNotificationApplication()
    .AddNotificationInfrastructure();

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
