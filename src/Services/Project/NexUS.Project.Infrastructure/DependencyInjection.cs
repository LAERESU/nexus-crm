using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NexUS.BuildingBlocks.Messaging;
using NexUS.BuildingBlocks.Messaging.Contracts;
using NexUS.Project.Application.Abstractions;
using NexUS.Project.Application.Common;
using NexUS.Project.Infrastructure.Messaging;
using NexUS.Project.Infrastructure.Persistence;
using NexUS.Project.Infrastructure.Security;
using Oracle.EntityFrameworkCore.Infrastructure;

namespace NexUS.Project.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Awalan nama antrean consumer service ini (<c>project.UserCreated</c>, ...).</summary>
    public const string ServiceName = "project";

    public static IServiceCollection AddProjectInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddDbContext<ProjectDbContext>(options => options.UseOracle(
            configuration.GetConnectionString("Oracle"),
            oracle => oracle.UseOracleSQLCompatibility(OracleSQLCompatibility.DatabaseVersion23)));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ProjectDbContext>());

        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IProjectReadService, ProjectReadService>();
        services.AddScoped<IProjectAccess, ProjectAccess>();
        services.AddScoped<IMilestoneRepository, MilestoneRepository>();
        services.AddScoped<IMilestoneReadService, MilestoneReadService>();
        services.AddScoped<IUserDirectory, UserDirectory>();

        // Read model user (USER_REF) dari event User Service; berhenti sendiri bila ConnectionStrings:RabbitMq kosong.
        services.AddNexusEventConsumer<UserCreated, UserRefConsumer>(ServiceName);
        services.AddNexusEventConsumer<UserUpdated, UserRefConsumer>(ServiceName);
        services.AddNexusEventConsumer<UserDeactivated, UserRefConsumer>(ServiceName);

        return services;
    }
}
