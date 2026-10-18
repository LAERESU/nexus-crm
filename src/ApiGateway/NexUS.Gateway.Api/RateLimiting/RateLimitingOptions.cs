namespace NexUS.Gateway.Api.RateLimiting;

/// <summary>Batas rate limit Gateway (ARCHITECTURE.md G.4). Nilai default sesuai tabel; bisa ditimpa per environment.</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; set; } = true;

    /// <summary><c>login</c>: IP + email, fixed window.</summary>
    public WindowLimit Login { get; set; } = new() { PermitLimit = 5, Window = TimeSpan.FromMinutes(15) };

    /// <summary><c>per-user</c>: klaim <c>sub</c>, sliding window.</summary>
    public WindowLimit PerUser { get; set; } = new() { PermitLimit = 300, Window = TimeSpan.FromMinutes(1) };

    /// <summary>Request anonim (tanpa token atau token tidak valid): IP klien, sliding window (pengganti per-user, G.4).</summary>
    public WindowLimit PerIp { get; set; } = new() { PermitLimit = 300, Window = TimeSpan.FromMinutes(1) };

    /// <summary><c>per-tenant</c>: klaim <c>tid</c>, token bucket (kapasitas = batas, terisi rata selama periode).</summary>
    public WindowLimit PerTenant { get; set; } = new() { PermitLimit = 5000, Window = TimeSpan.FromMinutes(1) };

    /// <summary><c>upload</c>: klaim <c>sub</c> untuk request multipart/form-data, fixed window.</summary>
    public WindowLimit Upload { get; set; } = new() { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) };
}

public sealed class WindowLimit
{
    public int PermitLimit { get; set; }
    public TimeSpan Window { get; set; }
}
