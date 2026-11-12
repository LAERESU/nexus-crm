using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NexUS.BuildingBlocks.Messaging;

/// <summary>
/// Menerjemahkan baris outbox (domain event internal service) menjadi kontrak event antar service.
/// </summary>
public interface IOutboxEventMapper
{
    /// <summary>
    /// Nama lengkap tipe domain event yang sudah punya kontrak. Relay hanya mengambil baris bertipe ini; event lain
    /// tetap tertunda di outbox sampai kontrak dan consumer-nya ada, bukan dibuang.
    /// </summary>
    IReadOnlyCollection<string> PublishedTypes { get; }

    IIntegrationEvent? Map(OutboxMessage message);
}

/// <summary>
/// Relay outbox: mengambil pesan yang belum terkirim dengan <c>FOR UPDATE SKIP LOCKED</c> (aman untuk beberapa replika),
/// mengirimnya ke broker, lalu menandai <c>PROCESSED_AT</c> dalam transaksi yang sama. Pengiriman at-least-once: bila
/// commit gagal setelah publish, pesan dikirim ulang, sehingga consumer wajib idempotent.
/// </summary>
public sealed class OutboxPublisherWorker<TContext>(
    IServiceScopeFactory scopes, RabbitMqConnection connection, ILogger<OutboxPublisherWorker<TContext>> logger)
    : BackgroundService where TContext : DbContext
{
    public const int BatchSize = 50;
    public const int MaxMappingRetries = 5;
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan FailureDelay = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Mengambil <see cref="BatchSize"/> baris tertua. Pengurutan di subquery dan ROWNUM di luarnya, karena ROWNUM
    /// dievaluasi sebelum ORDER BY; FETCH FIRST tidak dipakai karena Oracle menolaknya bersama FOR UPDATE (ORA-02014).
    /// Semua nilai dikirim sebagai bind variable; yang dirangkai hanya jumlah placeholder tipe.
    /// </summary>
    private static (string Sql, object[] Parameters) PendingQuery(IReadOnlyCollection<string> publishedTypes)
    {
        var parameters = new List<object> { MaxMappingRetries, BatchSize };
        var typeFilter = string.Join(" OR ", publishedTypes.Select(type =>
        {
            parameters.Add(type + ",%"); // TYPE berisi nama tipe lengkap diikuti nama assembly
            return $"TYPE LIKE {{{parameters.Count - 1}}}";
        }));

        var sql =
            "SELECT * FROM OUTBOX_MESSAGES WHERE ID IN (" +
            "SELECT ID FROM (SELECT ID FROM OUTBOX_MESSAGES WHERE PROCESSED_AT IS NULL AND RETRY_COUNT < {0} " +
            $"AND ({typeFilter}) ORDER BY OCCURRED_AT) WHERE ROWNUM <= {{1}}) FOR UPDATE SKIP LOCKED";
        return (sql, parameters.ToArray());
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!connection.IsConfigured)
        {
            logger.LogWarning("ConnectionStrings:RabbitMq kosong; relay outbox tidak berjalan dan event menumpuk di outbox");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = PollInterval;
            try
            {
                // Batch penuh berarti mungkin masih ada antrean: langsung lanjut tanpa menunggu.
                if (await PublishBatchAsync(stoppingToken) == BatchSize) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Broker atau database mati: data tetap aman di outbox dan dikirim setelah pulih.
                logger.LogWarning(ex, "Relay outbox gagal; mencoba lagi dalam {Delay}", FailureDelay);
                delay = FailureDelay;
            }

            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Mengirim satu batch; mengembalikan jumlah baris yang diambil.</summary>
    public async Task<int> PublishBatchAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var mapper = scope.ServiceProvider.GetRequiredService<IOutboxEventMapper>();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();

        if (mapper.PublishedTypes.Count == 0) return 0;

        var (sql, parameters) = PendingQuery(mapper.PublishedTypes);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var pending = await db.Set<OutboxMessage>().FromSqlRaw(sql, parameters).ToListAsync(ct);

        Exception? publishFailure = null;
        foreach (var message in pending.OrderBy(m => m.OccurredAt))
        {
            IIntegrationEvent? integrationEvent;
            try
            {
                integrationEvent = mapper.Map(message);
            }
            catch (Exception ex)
            {
                // Pesan rusak tidak boleh menahan pesan lain; setelah beberapa kali gagal ia dilewati dan perlu ditinjau.
                logger.LogError(ex, "Pesan outbox {OutboxId} ({OutboxType}) tidak dapat dipetakan", message.Id, message.Type);
                message.MarkFailed(ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message);
                continue;
            }

            if (integrationEvent is not null)
            {
                try
                {
                    await publisher.PublishAsync(integrationEvent, ct);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    publishFailure = ex; // sisanya tetap di outbox; yang sudah terkirim tetap ditandai
                    break;
                }
            }

            message.MarkProcessed();
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        if (publishFailure is not null) throw new InvalidOperationException("Event tidak dapat dikirim ke broker.", publishFailure);
        return pending.Count;
    }
}
