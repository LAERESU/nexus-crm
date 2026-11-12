using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexUS.BuildingBlocks.Auth;
using NexUS.BuildingBlocks.Caching;
using NexUS.BuildingBlocks.Messaging;
using NexUS.BuildingBlocks.Messaging.Contracts;
using NexUS.Auth.Application.Abstractions;
using NexUS.Auth.Application.Common;
using NexUS.Auth.Infrastructure.Messaging;
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
        services.AddSingleton<IAccessTokenReader, JwtAccessTokenReader>();

        services.AddNexusCaching(configuration);
        services.AddNexusTokenRevocation();

        // Auth memvalidasi access token miliknya sendiri dengan kunci lokal, bukan JWKS lewat HTTP ke dirinya.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .PostConfigure<JwtSigningKey>((options, signingKey) =>
            {
                options.ConfigurationManager = null;
                options.TokenValidationParameters.IssuerSigningKey = signingKey.Credentials.Key;
            });
        services.AddSingleton<IAccessTokenBlacklist, AccessTokenBlacklist>();

        // Undangan user (NX-021): UserCreated dari User Service -> kredensial tanpa password + email aktivasi.
        services.AddScoped<IProcessedEventStore, ProcessedEventStore>();
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.AddSingleton<IInvitationEmailSender, SmtpInvitationEmailSender>();
        services.AddNexusEventConsumer<UserCreated, UserCreatedHandler>("auth");

        services.Configure<DevSeedOptions>(configuration.GetSection(DevSeedOptions.SectionName));
        services.AddSingleton<DevSeeder>();
        services.AddHostedService(sp => sp.GetRequiredService<DevSeeder>());

        return services;
    }
}
