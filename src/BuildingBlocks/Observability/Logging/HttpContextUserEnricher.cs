using Microsoft.AspNetCore.Http;
using Serilog.Core;
using Serilog.Events;

namespace NexUS.BuildingBlocks.Observability;

/// <summary>
/// Menambahkan <c>TenantId</c> dan <c>UserId</c> dari user request yang sedang berjalan ke setiap log, dibaca saat
/// log ditulis. Karena itu tidak bergantung pada posisi middleware: log apa pun setelah autentikasi (termasuk log
/// error dari penanganan exception) memuat tenant dan user. Nama klaim mengikuti token Auth (ADR-0006).
/// </summary>
public sealed class HttpContextUserEnricher(IHttpContextAccessor accessor) : ILogEventEnricher
{
    public const string TenantClaim = "tid";
    public const string UserClaim = "sub";

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var user = accessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) return;

        if (user.FindFirst(TenantClaim)?.Value is { } tenantId)
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("TenantId", tenantId));
        if (user.FindFirst(UserClaim)?.Value is { } userId)
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("UserId", userId));
    }
}
