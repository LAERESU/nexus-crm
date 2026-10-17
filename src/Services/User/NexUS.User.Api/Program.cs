using NexUS.BuildingBlocks.Observability;
using NexUS.User.Api.Middleware;
using NexUS.User.Application;
using NexUS.User.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Host.AddNexusSerilog();

builder.Services
    .AddUserApplication()
    .AddUserInfrastructure();

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") });

app.Run();

public partial class Program;
