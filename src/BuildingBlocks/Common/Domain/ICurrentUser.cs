namespace NexUS.BuildingBlocks.Common.Domain;

public interface ICurrentUser
{
    long UserId { get; }
    long TenantId { get; }
    bool IsAuthenticated { get; }
    bool HasPermission(string permission);
}
