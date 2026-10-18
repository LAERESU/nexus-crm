using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;

namespace NexUS.BuildingBlocks.Auth;

public static class ServiceCollectionExtensions
{
    /// <summary>Kerangka validasi JWT RS256 via JWKS; diisi lengkap di NX-015.</summary>
    public static IServiceCollection AddNexusJwtAuth(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        return services;
    }

    /// <summary>Blacklist jti di cache (butuh <c>AddNexusCaching</c>).</summary>
    public static IServiceCollection AddNexusTokenRevocation(this IServiceCollection services)
    {
        services.AddSingleton<ITokenRevocationStore, CacheTokenRevocationStore>();
        return services;
    }
}
