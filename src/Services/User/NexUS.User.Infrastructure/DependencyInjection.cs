using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NexUS.BuildingBlocks.Auth;
using NexUS.BuildingBlocks.Caching;
using NexUS.User.Application.Abstractions;
using NexUS.User.Application.Common;
using NexUS.User.Infrastructure.Persistence;
using NexUS.User.Infrastructure.Security;
using NexUS.User.Infrastructure.Seeding;
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
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IRoleReadService, RoleReadService>();
        services.AddScoped<IUserPermissionReadService, UserPermissionReadService>();

        // Permission: User Service membaca dari database lokalnya (bukan HTTP ke dirinya) dan menghapus cache
        // setiap kali RoleChanged tersimpan (NX-020, ADR-0009).
        services.AddNexusCaching(configuration);
        services.TryAddSingleton<IPermissionCache, RedisPermissionCache>();
        services.AddScoped<IPermissionSource, DbPermissionSource>();
        services.AddScoped<ICurrentUserPermissions, CurrentUserPermissions>();

        services.Configure<DevSeedOptions>(configuration.GetSection(DevSeedOptions.SectionName));
        services.AddSingleton<DevSeeder>();
        services.AddHostedService(sp => sp.GetRequiredService<DevSeeder>());

        services.AddScoped<DefaultRoleProvisioner>();
        services.AddSingleton<DefaultRoleSeeder>();
        services.AddHostedService(sp => sp.GetRequiredService<DefaultRoleSeeder>());

        return services;
    }
}
