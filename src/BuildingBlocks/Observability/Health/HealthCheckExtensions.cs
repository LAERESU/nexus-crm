using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace NexUS.BuildingBlocks.Observability;

public static class HealthCheckExtensions
{
    public const string ReadyTag = "ready";

    /// <summary>
    /// Mendaftarkan pemeriksaan Oracle, Redis, dan RabbitMQ pada <c>/health/ready</c>. Dependency yang
    /// connection string-nya (<c>ConnectionStrings:Oracle|Redis|RabbitMq</c>) tidak dikonfigurasi dilewati.
    /// <c>/health/live</c> tidak memeriksa dependency.
    /// </summary>
    public static IServiceCollection AddNexusHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        var builder = services.AddHealthChecks();

        Add(configuration, "Oracle", cs => builder.AddCheck("oracle", new OracleHealthCheck(cs), HealthStatus.Unhealthy, [ReadyTag]));
        Add(configuration, "Redis", cs => builder.AddCheck("redis", new RedisHealthCheck(cs), HealthStatus.Unhealthy, [ReadyTag]));
        Add(configuration, "RabbitMq", cs => builder.AddCheck("rabbitmq", new RabbitMqHealthCheck(cs), HealthStatus.Unhealthy, [ReadyTag]));

        return services;
    }

    private static void Add(IConfiguration configuration, string name, Action<string> register)
    {
        var connectionString = configuration.GetConnectionString(name);
        if (!string.IsNullOrWhiteSpace(connectionString)) register(connectionString);
    }
}
