namespace NexUS.BuildingBlocks.Observability;

/// <summary>
/// Mode <c>--healthcheck</c> untuk HEALTHCHECK container. Image chiseled tidak punya shell atau curl,
/// sehingga aplikasi memeriksa dirinya sendiri lewat endpoint <c>/health/live</c>.
/// </summary>
public static class HealthProbe
{
    public const string Flag = "--healthcheck";
    public const string DefaultUrl = "http://localhost:8080/health/live";

    public static bool IsRequested(string[] args) => args.Contains(Flag);

    /// <returns>0 bila sehat, 1 bila tidak.</returns>
    public static async Task<int> RunAsync(string? url = null, CancellationToken ct = default)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var response = await client.GetAsync(url ?? DefaultUrl, ct);
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return 1;
        }
    }
}
