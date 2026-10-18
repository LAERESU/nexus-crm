using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using NexUS.BuildingBlocks.Caching;
using NexUS.BuildingBlocks.Common.Domain;

namespace NexUS.BuildingBlocks.Auth;

public sealed class NexusJwtOptions
{
    public const string SectionName = "Auth:Jwt";

    public string Issuer { get; set; } = "nexus-auth";
    public string Audience { get; set; } = "nexus-api";

    /// <summary>URL JWKS Auth Service, mis. <c>http://auth:8080/.well-known/jwks.json</c>.</summary>
    public string? JwksUrl { get; set; }

    /// <summary>False hanya untuk JWKS di jaringan internal (compose/Kubernetes) yang memakai http.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;
}

public static class ServiceCollectionExtensions
{
    public const string JwksHttpClient = "nexus-jwks";

    /// <summary>
    /// Validasi JWT RS256 dengan kunci dari JWKS Auth Service (ARCHITECTURE.md G.1, ADR-0006/0007): issuer, audience,
    /// masa berlaku dengan <see cref="NexusJwt.ClockSkew"/>, dan blacklist <c>jti</c>/<c>sid</c>. Semua endpoint wajib
    /// terautentikasi (fallback policy) kecuali yang ditandai <c>AllowAnonymous</c>.
    /// </summary>
    /// <param name="requireJwksUrl">
    /// False hanya untuk Auth Service, yang memvalidasi token dengan kunci miliknya sendiri tanpa JWKS lewat HTTP.
    /// </param>
    /// <param name="requireAuthenticationByDefault">
    /// False untuk Gateway: fallback policy juga berlaku untuk request tanpa endpoint, sehingga path tak terdaftar
    /// menjadi 401 alih-alih 404. Gateway menetapkan policy per route YARP (<c>default</c> atau <c>anonymous</c>).
    /// </param>
    public static IServiceCollection AddNexusJwtAuth(
        this IServiceCollection services, IConfiguration configuration, bool requireAuthenticationByDefault = true,
        bool requireJwksUrl = true)
    {
        services.AddOptions<NexusJwtOptions>()
            .Bind(configuration.GetSection(NexusJwtOptions.SectionName))
            // Tanpa JWKS semua token ditolak diam-diam; di luar Development aplikasi harus gagal start.
            .Validate<IHostEnvironment>(
                (options, environment) => !requireJwksUrl || environment.IsDevelopment() || !string.IsNullOrWhiteSpace(options.JwksUrl),
                "Auth:Jwt:JwksUrl wajib diisi di luar Development.")
            .ValidateOnStart();
        services.AddNexusCaching(configuration);
        services.AddNexusTokenRevocation();
        services.AddHttpClient(JwksHttpClient, client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddHttpContextAccessor();
        services.TryAddScoped<ICurrentUser, HttpCurrentUser>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<NexusJwtOptions>, IHttpClientFactory>((options, nexusOptions, httpClients) =>
                Configure(options, nexusOptions.Value, httpClients));

        services.AddAuthorization(options =>
        {
            if (requireAuthenticationByDefault)
                options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        });
        return services;
    }

    /// <summary>Blacklist jti/sid di Redis (butuh <c>AddNexusCaching</c>).</summary>
    public static IServiceCollection AddNexusTokenRevocation(this IServiceCollection services)
    {
        services.TryAddSingleton<ITokenRevocationStore, RedisTokenRevocationStore>();
        return services;
    }

    private static void Configure(JwtBearerOptions options, NexusJwtOptions nexus, IHttpClientFactory httpClients)
    {
        options.MapInboundClaims = false; // pertahankan nama klaim asli: sub, tid, sid, roles
        options.RequireHttpsMetadata = nexus.RequireHttpsMetadata;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = nexus.Issuer,
            ValidAudience = nexus.Audience,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ClockSkew = NexusJwt.ClockSkew,
            NameClaimType = JwtRegisteredClaimNames.Sub,
            RoleClaimType = "roles",
        };

        if (!string.IsNullOrWhiteSpace(nexus.JwksUrl))
        {
            // Konfigurasi dibangun langsung dari JWKS (tanpa OpenID discovery); cache dan refresh saat kid tidak
            // dikenal ditangani ConfigurationManager.
            options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                nexus.JwksUrl,
                new JwksConfigurationRetriever(nexus.Issuer),
                new HttpClientFactoryDocumentRetriever(httpClients, nexus.RequireHttpsMetadata))
            {
                // Kid baru (Auth restart/rotasi kunci) memicu ambil ulang JWKS paling cepat 30 detik sekali.
                RefreshInterval = TimeSpan.FromSeconds(30),
                AutomaticRefreshInterval = TimeSpan.FromHours(1),
            };
        }

        options.Events = new JwtBearerEvents
        {
            // Klien SignalR mengirim token lewat query string saat membuka WebSocket.
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"].ToString();
                if (!string.IsNullOrEmpty(token) && context.Request.Path.StartsWithSegments("/hubs"))
                    context.Token = token;
                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                var principal = context.Principal!;
                var jti = principal.FindFirstValue(JwtRegisteredClaimNames.Jti);
                if (!long.TryParse(principal.FindFirstValue(NexusJwt.TenantClaim), NumberStyles.None, CultureInfo.InvariantCulture, out var tenantId)
                    || string.IsNullOrWhiteSpace(jti))
                {
                    context.Fail("Token tidak memuat tenant atau jti.");
                    return;
                }

                var revocation = context.HttpContext.RequestServices.GetRequiredService<ITokenRevocationStore>();
                if (await revocation.IsRevokedAsync(tenantId, jti, principal.FindFirstValue(NexusJwt.SessionClaim), context.HttpContext.RequestAborted))
                    context.Fail("Token sudah dicabut.");
            },
        };
    }
}

/// <summary>
/// Mengambil dokumen lewat <see cref="IHttpClientFactory"/> setiap kali, agar rotasi handler (DNS baru saat pod
/// Auth berpindah) tetap berlaku; HttpClient tunggal di dalam ConfigurationManager singleton akan memakai koneksi lama selamanya.
/// </summary>
public sealed class HttpClientFactoryDocumentRetriever(IHttpClientFactory httpClients, bool requireHttps) : IDocumentRetriever
{
    public async Task<string> GetDocumentAsync(string address, CancellationToken cancel)
    {
        if (requireHttps && !address.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"JWKS wajib https: {address}", nameof(address));

        using var response = await httpClients.CreateClient(ServiceCollectionExtensions.JwksHttpClient).GetAsync(address, cancel);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancel);
    }
}

/// <summary>Membangun konfigurasi validasi dari dokumen JWKS polos (bukan OpenID discovery).</summary>
public sealed class JwksConfigurationRetriever(string issuer) : IConfigurationRetriever<OpenIdConnectConfiguration>
{
    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(string address, IDocumentRetriever retriever, CancellationToken cancel)
    {
        var jwks = new JsonWebKeySet(await retriever.GetDocumentAsync(address, cancel));
        var configuration = new OpenIdConnectConfiguration { Issuer = issuer, JsonWebKeySet = jwks };
        foreach (var key in jwks.GetSigningKeys()) configuration.SigningKeys.Add(key);
        return configuration;
    }
}

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    public Guid UserId => Guid.TryParse(User?.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : Guid.Empty;

    public long TenantId =>
        long.TryParse(User?.FindFirstValue(NexusJwt.TenantClaim), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0;

    public string? SessionId => User?.FindFirstValue(NexusJwt.SessionClaim);

    public IReadOnlyCollection<string> Roles => User?.FindAll("roles").Select(c => c.Value).ToArray() ?? [];
}
