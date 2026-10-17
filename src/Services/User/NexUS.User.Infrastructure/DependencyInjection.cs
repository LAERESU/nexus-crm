using Microsoft.Extensions.DependencyInjection;

namespace NexUS.User.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Titik registrasi Oracle, Redis, dan messaging; diisi saat story fitur service ini dikerjakan.</summary>
    public static IServiceCollection AddUserInfrastructure(this IServiceCollection services) => services;
}
