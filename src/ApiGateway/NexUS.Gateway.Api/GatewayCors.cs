namespace NexUS.Gateway.Api;

public static class GatewayCors
{
    public const string PolicyName = "frontend";
    public const string SectionName = "Cors:AllowedOrigins";

    /// <summary>Origin frontend yang diizinkan, dari konfigurasi per environment (tanpa wildcard).</summary>
    public static string[] AllowedOrigins(IConfiguration configuration) =>
        configuration.GetSection(SectionName).Get<string[]>() ?? [];
}
