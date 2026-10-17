namespace NexUS.Gateway.Api.Middleware;

/// <summary>Header keamanan untuk semua respons API (termasuk yang diteruskan dari service).</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
            headers.Remove("Server");
            headers.Remove("X-Powered-By");
            return Task.CompletedTask;
        });

        return next(context);
    }
}
