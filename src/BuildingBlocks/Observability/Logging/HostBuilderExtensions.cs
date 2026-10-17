using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Formatting.Compact;

namespace NexUS.BuildingBlocks.Observability;

public static class HostBuilderExtensions
{
    /// <summary>
    /// Serilog terstruktur (JSON ke console) dengan properti Service, Environment, dan CorrelationId.
    /// TenantId dan UserId ditambahkan oleh <see cref="ApplicationBuilderExtensions"/>.
    /// Nilai sensitif (password, token, dst.) disamarkan oleh <see cref="SensitiveDataMasker"/>.
    /// </summary>
    public static IHostBuilder AddNexusSerilog(this IHostBuilder host) =>
        host.UseSerilog((context, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", context.HostingEnvironment.ApplicationName)
            .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName)
            .Enrich.With<SensitiveDataMasker>()
            .WriteTo.Console(new RenderedCompactJsonFormatter()));
}
