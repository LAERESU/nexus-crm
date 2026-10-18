using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Formatting.Compact;

namespace NexUS.BuildingBlocks.Observability;

public static class HostBuilderExtensions
{
    /// <summary>
    /// Serilog terstruktur (JSON ke console) dengan properti Service, Environment, CorrelationId, serta TenantId dan
    /// UserId dari user yang login (<see cref="HttpContextUserEnricher"/>). Nilai sensitif disamarkan oleh
    /// <see cref="SensitiveDataMasker"/>.
    /// </summary>
    public static IHostBuilder AddNexusSerilog(this IHostBuilder host) =>
        host
            .ConfigureServices(services => services.AddHttpContextAccessor())
            .UseSerilog((context, services, configuration) => configuration
                .ReadFrom.Configuration(context.Configuration)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Service", context.HostingEnvironment.ApplicationName)
                .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName)
                .Enrich.With(new HttpContextUserEnricher(services.GetRequiredService<IHttpContextAccessor>()))
                .Enrich.With<SensitiveDataMasker>()
                .WriteTo.Console(new RenderedCompactJsonFormatter()));
}
