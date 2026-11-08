using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexUS.User.Application.Abstractions;
using NexUS.User.Application.Common;
using NexUS.User.Infrastructure.Persistence;
using Oracle.EntityFrameworkCore.Infrastructure;

namespace NexUS.User.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddUserInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);

        services.AddDbContext<UserDbContext>(options => options.UseOracle(
            configuration.GetConnectionString("Oracle"),
            oracle => oracle.UseOracleSQLCompatibility(OracleSQLCompatibility.DatabaseVersion23)));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<UserDbContext>());

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserReadService, UserReadService>();
        services.AddScoped<IDepartmentRepository, DepartmentRepository>();
        services.AddScoped<IDepartmentReadService, DepartmentReadService>();
        services.AddScoped<ITenantRepository, TenantRepository>();

        return services;
    }
}
