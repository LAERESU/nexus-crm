using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace NexUS.Gateway.Api.Middleware;

/// <summary>
/// Menolak (403) request lintas origin dari origin yang tidak diizinkan oleh policy CORS Gateway.
/// Middleware CORS bawaan hanya tidak menambahkan header CORS dan menyerahkan penolakan ke browser,
/// sedangkan request tetap diteruskan ke service; di sini request seperti itu dihentikan di Gateway.
/// Keputusan memakai policy CORS yang sama agar keduanya tidak pernah berbeda. Origin publik aplikasi
/// sendiri harus terdaftar di <c>Cors:AllowedOrigins</c> (tidak ada pengecualian "same-origin" karena
/// Host/scheme di belakang ingress tidak dapat dipercaya tanpa forwarded headers).
/// </summary>
public sealed class RejectDisallowedOriginMiddleware(RequestDelegate next, ICorsPolicyProvider policyProvider)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin))
        {
            await next(context);
            return;
        }

        var policy = await policyProvider.GetPolicyAsync(context, GatewayCors.PolicyName);
        if (policy is not null && policy.IsOriginAllowed(origin))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(
            new ProblemDetails { Status = StatusCodes.Status403Forbidden, Title = "Origin tidak diizinkan" },
            options: null, contentType: "application/problem+json", context.RequestAborted);
    }
}
