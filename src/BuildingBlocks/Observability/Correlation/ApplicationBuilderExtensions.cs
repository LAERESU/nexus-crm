using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Context;
using Serilog.Events;

namespace NexUS.BuildingBlocks.Observability;

public static class ApplicationBuilderExtensions
{
    public const string TenantClaim = "tenant_id";
    public const string UserClaim = ClaimTypes.NameIdentifier;

    /// <summary>Correlation id + log ringkas per request (tanpa header, query string, maupun body).</summary>
    public static IApplicationBuilder UseNexusObservability(this IApplicationBuilder app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseSerilogRequestLogging(options =>
        {
            // Probe health dipanggil terus-menerus; turunkan ke Verbose agar log tidak banjir.
            options.GetLevel = (context, _, exception) =>
                exception is not null || context.Response.StatusCode >= 500 ? LogEventLevel.Error
                : context.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Verbose
                : LogEventLevel.Information;
            options.EnrichDiagnosticContext = (diagnostics, context) =>
            {
                diagnostics.Set("TenantId", context.User.FindFirstValue(TenantClaim));
                diagnostics.Set("UserId", context.User.FindFirstValue(UserClaim));
            };
        });
        return app;
    }

    /// <summary>Tambahkan TenantId dan UserId ke semua log di dalam request. Pasang setelah autentikasi.</summary>
    public static IApplicationBuilder UseNexusLogEnrichment(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            using (LogContext.PushProperty("TenantId", context.User.FindFirstValue(TenantClaim)))
            using (LogContext.PushProperty("UserId", context.User.FindFirstValue(UserClaim)))
            {
                await next(context);
            }
        });

    /// <summary>Teruskan X-Correlation-Id pada HttpClient ini.</summary>
    public static IHttpClientBuilder AddCorrelationIdPropagation(this IHttpClientBuilder builder)
    {
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddTransient<CorrelationIdDelegatingHandler>();
        return builder.AddHttpMessageHandler<CorrelationIdDelegatingHandler>();
    }
}
