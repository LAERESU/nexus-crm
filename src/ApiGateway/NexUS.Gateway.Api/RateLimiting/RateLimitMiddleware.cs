using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NexUS.BuildingBlocks.Caching;

namespace NexUS.Gateway.Api.RateLimiting;

/// <summary>
/// Menerapkan kebijakan ARCHITECTURE.md G.4 di Gateway, setelah autentikasi (agar <c>sub</c>/<c>tid</c> tersedia).
/// User terautentikasi: <c>per-user</c>, <c>upload</c>, lalu <c>per-tenant</c>. Request anonim (termasuk token tidak
/// valid): batas per IP, ditambah <c>login</c> untuk POST /api/v1/auth/login. Ditolak → 429 + <c>Retry-After</c>.
/// </summary>
public sealed class RateLimitMiddleware(
    RequestDelegate next,
    RedisRateLimiter limiter,
    IOptionsMonitor<RateLimitingOptions> options,
    ILogger<RateLimitMiddleware> logger)
{
    public const string LoginPath = "/api/v1/auth/login";
    private const int MaxLoginBodyBytes = 16 * 1024;

    public async Task InvokeAsync(HttpContext context)
    {
        var settings = options.CurrentValue;
        if (settings.Enabled)
        {
            var decision = await limiter.EvaluateAsync(await RulesAsync(context, settings));
            if (!decision.Allowed)
            {
                await RejectAsync(context, decision);
                return;
            }
        }

        await next(context);
    }

    private static async Task<IReadOnlyList<RateLimitRule>> RulesAsync(HttpContext context, RateLimitingOptions settings)
    {
        var request = context.Request;
        var user = context.User;

        if (user.Identity?.IsAuthenticated == true
            && long.TryParse(user.FindFirstValue("tid"), NumberStyles.None, CultureInfo.InvariantCulture, out var tenantId)
            && user.FindFirstValue("sub") is { Length: > 0 } userId)
        {
            // Urutan menentukan kebijakan yang dilaporkan; kuota hanya dipakai bila semuanya lolos (satu skrip atomik).
            var rules = new List<RateLimitRule>
            {
                new("per-user", RateLimitAlgorithm.SlidingLog, CacheKeys.Tenant(tenantId, $"rl:user:{userId}"), settings.PerUser),
            };
            if (request.ContentType?.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase) == true)
                rules.Add(new("upload", RateLimitAlgorithm.FixedWindow, CacheKeys.Tenant(tenantId, $"rl:upload:{userId}"), settings.Upload));
            rules.Add(new("per-tenant", RateLimitAlgorithm.TokenBucket, CacheKeys.Tenant(tenantId, "rl:tenant"), settings.PerTenant));
            return rules;
        }

        // Anonim (tanpa token atau token tidak valid): dibatasi per IP sebagai pengganti per-user (ARCHITECTURE.md G.4).
        // Key global karena tenant belum diketahui (ADR-0008).
        var ip = ClientIp(context);
        var anonymous = new List<RateLimitRule>
        {
            new("per-ip", RateLimitAlgorithm.SlidingLog, $"rl:ip:{Hash(ip)}", settings.PerIp),
        };

        // StartsWithSegments: /login, /login/ dan variasi huruf besar-kecil sama-sama terkena batas login.
        if (HttpMethods.IsPost(request.Method) && request.Path.StartsWithSegments(LoginPath, StringComparison.OrdinalIgnoreCase))
        {
            var email = await ReadLoginEmailAsync(request);
            anonymous.Insert(0, new("login", RateLimitAlgorithm.FixedWindow, $"rl:login:{Hash($"{ip}|{email}")}", settings.Login));
        }

        return anonymous;
    }

    private async Task RejectAsync(HttpContext context, RateLimitDecision decision)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(decision.RetryAfter.TotalSeconds));
        logger.LogInformation("Rate limit {Policy} terlampaui; coba lagi dalam {RetryAfterSeconds} detik", decision.Policy, seconds);

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

    /// <summary>
    /// Membaca email login dengan aturan yang sama seperti Auth (System.Text.Json web: nama properti tidak peka huruf
    /// besar-kecil, properti terakhir yang menang), tanpa menghabiskan stream. Body tanpa Content-Length (chunked) atau
    /// lebih dari 16 KB tidak dibaca dan dikunci per IP saja, agar Gateway tidak bisa dipaksa menyangga body besar.
    /// </summary>
    private static async Task<string> ReadLoginEmailAsync(HttpRequest request)
    {
        if (request.ContentLength is not (> 0 and <= MaxLoginBodyBytes)
            || request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true)
            return "";

        request.EnableBuffering();
        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: request.HttpContext.RequestAborted);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return "";

            string? email = null;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name.Equals("email", StringComparison.OrdinalIgnoreCase))
                    email = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
            }

            return email?.Trim().ToLowerInvariant() ?? "";
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
