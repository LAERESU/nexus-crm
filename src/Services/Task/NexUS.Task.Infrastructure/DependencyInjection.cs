using Microsoft.Extensions.DependencyInjection;

namespace NexUS.Tasks.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Titik registrasi Oracle, Redis, dan messaging; diisi saat story fitur service ini dikerjakan.</summary>
    public static IServiceCollection AddTaskInfrastructure(this IServiceCollection services) => services;
}
