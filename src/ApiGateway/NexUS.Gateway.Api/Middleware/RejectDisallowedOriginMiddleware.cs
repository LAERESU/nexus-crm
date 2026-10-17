using Microsoft.AspNetCore.Mvc;

namespace NexUS.Gateway.Api.Middleware;

/// <summary>
/// Menolak (403) request lintas origin dari origin yang tidak terdaftar. Middleware CORS bawaan hanya
/// tidak menambahkan header CORS dan menyerahkan penolakan ke browser, sedangkan request tetap
/// diteruskan ke service; di sini request seperti itu dihentikan di Gateway.
/// </summary>
public sealed class RejectDisallowedOriginMiddleware(RequestDelegate next, IConfiguration configuration)
{
    private readonly HashSet<string> _allowed = new(GatewayCors.AllowedOrigins(configuration), StringComparer.OrdinalIgnoreCase);

    public async Task InvokeAsync(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin) || _allowed.Contains(origin) || IsSameOrigin(context, origin))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(
            new ProblemDetails { Status = StatusCodes.Status403Forbidden, Title = "Origin tidak diizinkan" },
            options: null, contentType: "application/problem+json", context.RequestAborted);
    }

    private static bool IsSameOrigin(HttpContext context, string origin) =>
        string.Equals(origin, $"{context.Request.Scheme}://{context.Request.Host}", StringComparison.OrdinalIgnoreCase);
}
