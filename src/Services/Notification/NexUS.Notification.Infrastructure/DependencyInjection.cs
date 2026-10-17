using Microsoft.Extensions.DependencyInjection;

namespace NexUS.Notification.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Titik registrasi Oracle, Redis, dan messaging; diisi saat story fitur service ini dikerjakan.</summary>
    public static IServiceCollection AddNotificationInfrastructure(this IServiceCollection services) => services;
}
