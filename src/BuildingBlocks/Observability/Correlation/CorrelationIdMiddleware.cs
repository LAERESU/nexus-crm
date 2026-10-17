using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace NexUS.BuildingBlocks.Observability;

public static class CorrelationId
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "NexUS.CorrelationId";
    public const string LogProperty = "CorrelationId";

    /// <summary>Hanya menerima id yang aman ditulis ke log (huruf, angka, '-', '_'; maksimum 64 karakter).</summary>
    public static bool IsValid(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= 64 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}

/// <summary>Membaca atau membuat X-Correlation-Id, mengembalikannya di response, dan menyertakannya di setiap log.</summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[CorrelationId.HeaderName].FirstOrDefault();
        var correlationId = CorrelationId.IsValid(incoming) ? incoming! : Guid.NewGuid().ToString("N");

        context.Items[CorrelationId.ItemKey] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationId.HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty(CorrelationId.LogProperty, correlationId))
        {
            await next(context);
        }
    }
}

/// <summary>Meneruskan X-Correlation-Id dari request yang sedang diproses ke panggilan HttpClient antar service.</summary>
public sealed class CorrelationIdDelegatingHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!request.Headers.Contains(CorrelationId.HeaderName) &&
            accessor.HttpContext?.Items[CorrelationId.ItemKey] is string correlationId)
        {
            request.Headers.TryAddWithoutValidation(CorrelationId.HeaderName, correlationId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
