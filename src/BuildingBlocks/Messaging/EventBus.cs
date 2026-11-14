using System.Text.Json;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace NexUS.BuildingBlocks.Messaging;

/// <summary>Mengirim event ke broker. Hanya dipanggil relay outbox, bukan handler (CONTEXT.md bagian 3 poin 2).</summary>
public interface IEventPublisher
{
    /// <summary>Selesai setelah broker mengonfirmasi pesan; melempar exception bila broker tidak terjangkau.</summary>
    Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken ct = default);
}

/// <summary>Handler satu jenis event. Wajib idempotent: event yang sama bisa datang lebih dari sekali.</summary>
public interface IIntegrationEventHandler<in TEvent> where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken ct);
}

public static class EventBusTopology
{
    /// <summary>Satu topic exchange untuk semua event; routing key = nama event (mis. <c>UserCreated</c>).</summary>
    public const string Exchange = "nexus.events";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string RoutingKey(Type eventType) => eventType.Name;

    /// <summary>
    /// Alternate exchange <see cref="Exchange"/>: event yang belum punya antrean penerima (consumer belum pernah berjalan,
    /// atau memang belum ada) diparkir di antrean <see cref="UnroutedQueue"/> selama <see cref="UnroutedTtl"/>, sehingga
    /// tidak hilang diam-diam dan tidak menahan relay outbox (ADR-0012).
    /// </summary>
    public const string UnroutedExchange = "nexus.unrouted";

    public const string UnroutedQueue = "nexus.unrouted";

    public static readonly TimeSpan UnroutedTtl = TimeSpan.FromDays(7);

    /// <summary>Header jumlah percobaan pemrosesan pesan di consumer.</summary>
    public const string AttemptsHeader = "x-nexus-attempts";

    /// <summary>Antrean pesan yang gagal diproses berulang kali (dead-letter) milik satu antrean consumer.</summary>
    public static string DeadLetterQueue(string queue) => $"{queue}.dead";

    /// <summary>
    /// Mendeklarasikan exchange utama beserta alternate exchange-nya. Argumen exchange harus sama di semua pemanggil;
    /// broker yang sudah punya <c>nexus.events</c> tanpa alternate exchange menolak deklarasi ini (lihat ADR-0012).
    /// </summary>
    public static async Task DeclareExchangeAsync(IChannel channel, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(UnroutedExchange, ExchangeType.Fanout, durable: true, autoDelete: false, cancellationToken: ct);
        await channel.QueueDeclareAsync(
            UnroutedQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-message-ttl"] = (long)UnroutedTtl.TotalMilliseconds },
            cancellationToken: ct);
        await channel.QueueBindAsync(UnroutedQueue, UnroutedExchange, string.Empty, cancellationToken: ct);

        await channel.ExchangeDeclareAsync(
            Exchange, ExchangeType.Topic, durable: true, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["alternate-exchange"] = UnroutedExchange },
            cancellationToken: ct);
    }
}

/// <summary>Satu koneksi RabbitMQ per proses, dibuat saat pertama dipakai dan dibuat ulang bila tertutup.</summary>
public sealed class RabbitMqConnection(string? amqpUri, string clientName) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    /// <summary>
    /// False bila <c>ConnectionStrings:RabbitMq</c> kosong: relay dan consumer tidak berjalan (event tetap tersimpan
    /// di outbox), sehingga service tetap bisa hidup tanpa broker.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(amqpUri);

    public async Task<IConnection> GetAsync(CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("ConnectionStrings:RabbitMq belum dikonfigurasi.");
        if (_connection is { IsOpen: true } open) return open;

        await _gate.WaitAsync(ct);
        try
        {
            if (_connection is { IsOpen: true } existing) return existing;

            if (_connection is not null) await _connection.DisposeAsync();
            var factory = new ConnectionFactory { Uri = new Uri(amqpUri!), ClientProvidedName = clientName, AutomaticRecoveryEnabled = true };
            return _connection = await factory.CreateConnectionAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Batas waktu menutup koneksi saat service berhenti.</summary>
    public static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(5);

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            // Koneksi ke broker yang sempat macet bisa tidak pernah menjawab permintaan tutup; jangan sampai menahan
            // service berhenti tanpa batas.
            var close = CloseAsync(_connection);
            await Task.WhenAny(close, Task.Delay(CloseTimeout * 2));
        }

        _gate.Dispose();
    }

    private static async Task CloseAsync(IConnection connection)
    {
        try
        {
            await connection.AbortAsync(CloseTimeout);
            await connection.DisposeAsync();
        }
        catch (Exception)
        {
            // Service sedang berhenti; kegagalan menutup koneksi tidak perlu diteruskan.
        }
    }
}

public sealed class RabbitMqEventPublisher(RabbitMqConnection connection, ILogger<RabbitMqEventPublisher> logger) : IEventPublisher
{
    /// <summary>Batas waktu satu publish, agar broker yang macet (bukan mati) tidak menahan relay tanpa batas.</summary>
    public static readonly TimeSpan PublishTimeout = TimeSpan.FromSeconds(15);

    public async Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(PublishTimeout);
        var ct = timeout.Token;

        // Publisher confirm + mandatory: BasicPublishAsync baru selesai setelah broker menerima pesan. Event tanpa antrean
        // penerima diterima alternate exchange (diparkir), jadi mandatory hanya gagal bila alternate exchange pun tidak
        // ada; outbox ditandai terkirim hanya bila broker mengonfirmasi.
        var options = new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true);
        await using var channel = await (await connection.GetAsync(ct)).CreateChannelAsync(options, ct);
        await EventBusTopology.DeclareExchangeAsync(channel, ct);

        var type = integrationEvent.GetType();
        var properties = new BasicProperties
        {
            MessageId = integrationEvent.EventId.ToString("D"),
            Type = type.Name,
            ContentType = "application/json",
            Persistent = true,
            Timestamp = new AmqpTimestamp(integrationEvent.OccurredAt.ToUnixTimeSeconds()),
        };
        var body = JsonSerializer.SerializeToUtf8Bytes(integrationEvent, type, EventBusTopology.Json);

        await channel.BasicPublishAsync(EventBusTopology.Exchange, EventBusTopology.RoutingKey(type), mandatory: true, properties, body, ct);
        logger.LogDebug("Event {EventType} {EventId} terkirim", type.Name, integrationEvent.EventId);
    }
}
