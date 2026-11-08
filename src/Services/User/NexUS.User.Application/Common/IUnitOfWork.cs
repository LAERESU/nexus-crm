namespace NexUS.User.Application.Common;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Menetapkan versi yang diharapkan klien (header <c>If-Match</c>). Bila baris sudah berubah,
    /// <see cref="SaveChangesAsync"/> melempar <c>ConcurrencyException</c> (409).
    /// </summary>
    void ExpectRowVersion(object entity, long expectedRowVersion);
}
