namespace NexUS.Auth.Application.Common;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Menjalankan beberapa perubahan (termasuk UPDATE massal) dalam satu transaksi database.</summary>
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken ct = default);
}
