using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NexUS.BuildingBlocks.Messaging;
using NexUS.BuildingBlocks.Messaging.Contracts;
using NexUS.Tasks.Application.Abstractions;
using NexUS.Tasks.Application.Common;
using NexUS.Tasks.Infrastructure.Messaging;
using NexUS.Tasks.Infrastructure.Persistence;
using NexUS.Tasks.Infrastructure.Security;
using Oracle.EntityFrameworkCore.Infrastructure;

namespace NexUS.Tasks.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Awalan nama antrean consumer service ini (<c>task.ProjectCreated</c>, ...).</summary>
    public const string ServiceName = "task";

    public static IServiceCollection AddTaskInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddDbContext<TaskDbContext>(options => options.UseOracle(
            configuration.GetConnectionString("Oracle"),
            oracle => oracle.UseOracleSQLCompatibility(OracleSQLCompatibility.DatabaseVersion23)));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<TaskDbContext>());

        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<TaskReadService>();
        services.AddScoped<ITaskReadService>(sp => sp.GetRequiredService<TaskReadService>());
        services.AddScoped<IProjectDirectory, ProjectDirectory>();
        services.AddScoped<ITaskAccess, TaskAccess>();

        // Read model proyek dari event Project Service (NX-032); berhenti sendiri bila ConnectionStrings:RabbitMq kosong.
        services.AddNexusEventConsumer<ProjectCreated, ProjectCreatedHandler>(ServiceName);
        services.AddNexusEventConsumer<ProjectUpdated, ProjectUpdatedHandler>(ServiceName);
        services.AddNexusEventConsumer<ProjectArchived, ProjectArchivedHandler>(ServiceName);
        services.AddNexusEventConsumer<MemberAdded, MemberAddedHandler>(ServiceName);
        services.AddNexusEventConsumer<MemberRoleChanged, MemberRoleChangedHandler>(ServiceName);
        services.AddNexusEventConsumer<MemberRemoved, MemberRemovedHandler>(ServiceName);

        return services;
    }
}
