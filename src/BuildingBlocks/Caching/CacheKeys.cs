namespace NexUS.BuildingBlocks.Caching;

public static class CacheKeys
{
    /// <summary>Setiap key wajib berawalan <c>tnt:{tenantId}:</c> (CONTEXT.md bagian 3 poin 8).</summary>
    public static string Tenant(long tenantId, string key) => $"tnt:{tenantId}:{key}";
}
