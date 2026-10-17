using Microsoft.Extensions.Hosting;
using Serilog;

namespace NexUS.BuildingBlocks.Observability;

public static class HostBuilderExtensions
{
    /// <summary>Serilog terstruktur; enricher correlation id dan sink lain ditambah di NX-010.</summary>
    public static IHostBuilder AddNexusSerilog(this IHostBuilder host) =>
        host.UseSerilog((context, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console());
}
