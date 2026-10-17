namespace NexUS.BuildingBlocks.Common.Results;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int Size, long TotalCount)
{
    public const int DefaultSize = 20;
    public const int MaxSize = 100;

    public static int NormalizePage(int page) => page < 1 ? 1 : page;
    public static int NormalizeSize(int size) => size < 1 ? DefaultSize : Math.Min(size, MaxSize);
    public static int Skip(int page, int size) => (NormalizePage(page) - 1) * NormalizeSize(size);
}
