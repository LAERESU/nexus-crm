using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace NexUS.Gateway.Api.RateLimiting;

/// <summary>
/// Menerapkan kebijakan ARCHITECTURE.md G.4 di Gateway, setelah autentikasi (agar <c>sub</c>/<c>tid</c> tersedia):
/// <c>login</c> untuk POST /api/v1/auth/login, lalu <c>per-tenant</c>, <c>per-user</c>, dan <c>upload</c> untuk user
/// yang terautentikasi. Request yang ditolak mendapat 429 + <c>Retry-After</c>.
/// </summary>
public sealed class RateLimitMiddleware(
    RequestDelegate next,
    RedisRateLimiter limiter,
    IOptionsMonitor<RateLimitingOptions> options,
    ILogger<RateLimitMiddleware> logger)
{
    public const string LoginPath = "/api/v1/auth/login";

    public async Task InvokeAsync(HttpContext context)
    {
        var settings = options.CurrentValue;
        if (!settings.Enabled)
        {
            await next(context);
            return;
        }

        var decision = await EvaluateAsync(context, settings);
        if (decision is { } rejected)
        {
            await RejectAsync(context, rejected.Policy, rejected.Decision.RetryAfter);
            return;
        }

        await next(context);
    }

    private async Task<(string Policy, RateLimitDecision Decision)?> EvaluateAsync(HttpContext context, RateLimitingOptions settings)
    {
        var request = context.Request;

        if (HttpMethods.IsPost(request.Method) && request.Path.Equals(LoginPath, StringComparison.OrdinalIgnoreCase))
        {
            // Kunci IP + email; email di-hash karena data pribadi. Key global (belum ada tenant saat login).
            var email = await ReadLoginEmailAsync(request);
            var key = $"rl:login:{Hash($"{ClientIp(context)}|{email}")}";
            var login = await limiter.FixedWindowAsync(key, settings.Login);
            if (!login.Allowed) return ("login", login);
        }

        var user = context.User;
        if (user.Identity?.IsAuthenticated != true) return null;

        var tenantId = user.FindFirstValue("tid");
        var userId = user.FindFirstValue("sub");
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(userId)) return null;

        var tenant = await limiter.TokenBucketAsync($"tnt:{tenantId}:rl:tenant", settings.PerTenant);
        if (!tenant.Allowed) return ("per-tenant", tenant);

        var perUser = await limiter.SlidingWindowAsync($"tnt:{tenantId}:rl:user:{userId}", settings.PerUser);
        if (!perUser.Allowed) return ("per-user", perUser);

        if (request.HasFormContentType && request.ContentType?.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase) == true)
        {
            var upload = await limiter.FixedWindowAsync($"tnt:{tenantId}:rl:upload:{userId}", settings.Upload);
            if (!upload.Allowed) return ("upload", upload);
        }

        return null;
    }

    private async Task RejectAsync(HttpContext context, string policy, TimeSpan retryAfter)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        logger.LogInformation("Rate limit {Policy} terlampaui; coba lagi dalam {RetryAfterSeconds} detik", policy, seconds);

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        await context.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Terlalu banyak permintaan",
                Detail = $"Batas permintaan terlampaui. Coba lagi dalam {seconds} detik.",
            },
            options: null, contentType: "application/problem+json", context.RequestAborted);
    }

    /// <summary>Membaca email dari body JSON login tanpa menghabiskan stream (body tetap diteruskan ke Auth).</summary>
    private static async Task<string> ReadLoginEmailAsync(HttpRequest request)
    {
        if (request.ContentLength is > 16 * 1024 || request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true)
            return "";

        request.EnableBuffering();
        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: request.HttpContext.RequestAborted);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("email", out var email)
                   && email.ValueKind == JsonValueKind.String
                ? email.GetString()!.Trim().ToLowerInvariant()
                : "";
        }
        catch (JsonException)
        {
            return "";
        }
        finally
        {
            request.Body.Position = 0;
        }
    }

    private static string ClientIp(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..32];
}
