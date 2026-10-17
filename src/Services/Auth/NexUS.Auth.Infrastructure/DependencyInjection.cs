using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexUS.Auth.Application.Abstractions;
using NexUS.Auth.Application.Common;
using NexUS.Auth.Infrastructure.Persistence;
using NexUS.Auth.Infrastructure.Security;
using NexUS.Auth.Infrastructure.Seeding;
using Oracle.EntityFrameworkCore.Infrastructure;

namespace NexUS.Auth.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAuthInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);

        services.AddDbContext<AuthDbContext>(options => options.UseOracle(
            configuration.GetConnectionString("Oracle"),
            oracle => oracle.UseOracleSQLCompatibility(OracleSQLCompatibility.DatabaseVersion23)));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AuthDbContext>());

        services.AddScoped<ICredentialRepository, CredentialRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<ILoginAttemptRepository, LoginAttemptRepository>();
        services.AddScoped<IUserDirectory, CredentialRolesUserDirectory>();

        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.AddSingleton<JwtSigningKey>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();

        services.Configure<DevSeedOptions>(configuration.GetSection(DevSeedOptions.SectionName));
        services.AddHostedService<DevSeeder>();

        return services;
    }
}
