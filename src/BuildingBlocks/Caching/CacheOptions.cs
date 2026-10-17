namespace NexUS.BuildingBlocks.Caching;

public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    public int DefaultTtlMinutes { get; set; } = 10;
    public string KeyPrefix { get; set; } = string.Empty;
}
