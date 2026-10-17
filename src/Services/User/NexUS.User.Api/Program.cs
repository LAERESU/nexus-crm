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
