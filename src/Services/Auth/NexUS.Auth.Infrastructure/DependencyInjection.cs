using Microsoft.Extensions.DependencyInjection;

namespace NexUS.Auth.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Titik registrasi Oracle, Redis, dan messaging; diisi saat story fitur service ini dikerjakan.</summary>
    public static IServiceCollection AddAuthInfrastructure(this IServiceCollection services) => services;
}
