namespace NexUS.Gateway.Api;

public static class GatewayRoutes
{
    /// <summary>
    /// Path milik komunikasi antar service yang diblokir di Gateway (selalu 404). Tulis segmen resource secara
    /// literal: pola dengan parameter di posisi resource kalah prioritas dari route YARP dan tidak akan memblokir.
    /// </summary>
    public static readonly string[] InternalPaths =
    [
        "/api/v1/users/{id}/permissions",
    ];

    /// <summary>
    /// Memastikan setiap route YARP menyatakan <c>AuthorizationPolicy</c> (<c>default</c> atau <c>anonymous</c>).
    /// Tanpa itu route menjadi publik karena Gateway tidak memakai fallback policy.
    /// </summary>
    public static void EnsureAuthorizationPolicies(IConfiguration configuration)
    {
        var missing = configuration.GetSection("ReverseProxy:Routes").GetChildren()
            .Where(route => string.IsNullOrWhiteSpace(route["AuthorizationPolicy"]))
            .Select(route => route.Key)
            .ToList();

        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Route YARP tanpa AuthorizationPolicy (isi \"default\" atau \"anonymous\"): {string.Join(", ", missing)}");
    }
}
