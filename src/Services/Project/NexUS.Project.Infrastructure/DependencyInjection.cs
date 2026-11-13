using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NexUS.Project.Application.Abstractions;
using NexUS.Project.Application.Common;
using NexUS.Project.Infrastructure.Persistence;
using NexUS.Project.Infrastructure.Security;
using Oracle.EntityFrameworkCore.Infrastructure;

namespace NexUS.Project.Infrastructure;

public static class DependencyInjection
{
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

        return services;
    }
}
