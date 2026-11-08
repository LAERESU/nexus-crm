using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using NexUS.BuildingBlocks.Caching;

namespace NexUS.BuildingBlocks.Auth;

public sealed class PermissionRequirement(string permission, bool globalOnly = false) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;

    /// <summary>True untuk endpoint yang berlaku di seluruh tenant: hanya penugasan role global yang dihitung.</summary>
    public bool GlobalOnly { get; } = globalOnly;
}

/// <summary>
/// Memenuhi <see cref="PermissionRequirement"/> bila user memegang permission itu pada scope mana pun. Scope
/// (departemen/proyek) diperiksa lagi oleh service pemilik sumber daya (pemeriksaan kepemilikan objek). Bila
/// permission tidak dapat diambil, akses ditolak (fail closed). <see cref="IPermissionService"/> diambil saat dibutuhkan
/// saja: handler ikut dibuat di setiap request, dan endpoint tanpa pemeriksaan permission (mis. health) tidak boleh
/// ikut membentuk koneksi database/Redis.
/// </summary>
public sealed class PermissionHandler(IServiceProvider services, ILogger<PermissionHandler> logger)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var principal = context.User;
        if (principal.Identity?.IsAuthenticated != true
            || !Guid.TryParse(principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId) || userId == Guid.Empty
            || !long.TryParse(principal.FindFirst(NexusJwt.TenantClaim)?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var tenantId))
            return;

        try
        {
            var ct = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;
            var granted = await services.GetRequiredService<IPermissionService>().GetAsync(tenantId, userId, ct);
            if (granted.Any(p => string.Equals(p.Code, requirement.Permission, StringComparison.Ordinal)
                    && (!requirement.GlobalOnly || p.ScopeType == "global")))
                context.Succeed(requirement);
        }
        catch (Exception ex) when (!(context.Resource as HttpContext)?.RequestAborted.IsCancellationRequested ?? true)
        {
            // Termasuk timeout HttpClient (TaskCanceledException); hanya pembatalan oleh klien yang diteruskan.
            logger.LogError(ex, "Permission {Permission} tidak dapat diperiksa; akses ditolak", requirement.Permission);
        }
    }
}

/// <summary>
/// Membuat policy dinamis untuk nama berformat <c>modul.aksi</c> (mis. <c>[Authorize(Policy = "sop.approve")]</c>).
/// Nama lain yang tidak terdaftar tetap gagal keras (bukan diam-diam menolak), agar salah ketik cepat ketahuan.
/// </summary>
public sealed partial class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    private readonly ConcurrentDictionary<string, AuthorizationPolicy> _policies = new();

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (await base.GetPolicyAsync(policyName) is { } registered) return registered;
        if (!PermissionName().IsMatch(policyName)) return null;

        return _policies.GetOrAdd(policyName, name => new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(name))
            .Build());
    }

    [GeneratedRegex("^[a-z][a-z0-9]*(\\.[a-z][a-z0-9]*)+$")]
    private static partial Regex PermissionName();
}

public sealed class UserServiceOptions
{
    public const string SectionName = "Services:User";

    /// <summary>URL dasar User Service, mis. <c>http://user:8080</c>.</summary>
    public string? BaseUrl { get; set; }

    public int TimeoutSeconds { get; set; } = 5;
}

/// <summary>
/// Sumber permission untuk service selain User Service: memanggil <c>GET /api/v1/users/{id}/permissions</c> dengan
/// token user itu sendiri (endpoint itu hanya meminta login dan mengizinkan user membaca miliknya sendiri).
/// </summary>
public sealed class UserServicePermissionSource(IHttpClientFactory httpClients, IHttpContextAccessor accessor) : IPermissionSource
{
    public const string HttpClientName = "nexus-user-service";

    private sealed record Response(List<GrantedPermission>? Permissions);

    public async Task<IReadOnlyList<GrantedPermission>> GetAsync(long tenantId, Guid userId, CancellationToken ct = default)
    {
        var authorization = accessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
            throw new InvalidOperationException("Tidak ada token untuk diteruskan ke User Service.");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/users/{userId:D}/permissions");
        request.Headers.TryAddWithoutValidation("Authorization", authorization);

        using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, ct);
        // 404 juga dianggap gagal (fail closed, tidak di-cache): bisa berarti BaseUrl/route salah atau user belum diprovisi.
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Response>(ct))?.Permissions ?? [];
    }
}

public static class PermissionServiceCollectionExtensions
{
    /// <summary>
    /// Policy permission dinamis + cache Redis (NX-020). Pasang setelah <c>AddNexusJwtAuth</c>. Service pemilik data
    /// permission (User) mendaftarkan <see cref="IPermissionSource"/> miliknya lebih dulu; service lain otomatis
    /// memakai <see cref="UserServicePermissionSource"/> (butuh <c>Services:User:BaseUrl</c>).
    /// </summary>
    public static IServiceCollection AddNexusPermissionAuthorization(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddNexusCaching(configuration);
        services.Configure<UserServiceOptions>(configuration.GetSection(UserServiceOptions.SectionName));
        services.AddHttpContextAccessor();

        services.TryAddSingleton<IPermissionCache, RedisPermissionCache>();
        services.TryAddScoped<IPermissionSource, UserServicePermissionSource>();
        services.TryAddScoped<IPermissionService, PermissionService>();

        services.AddHttpClient(UserServicePermissionSource.HttpClientName)
            .ConfigureHttpClient((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<UserServiceOptions>>().Value;
                if (!string.IsNullOrWhiteSpace(options.BaseUrl)) client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds));
            });

        services.Replace(ServiceDescriptor.Singleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>());
        services.AddScoped<IAuthorizationHandler, PermissionHandler>();
        return services;
    }
}
