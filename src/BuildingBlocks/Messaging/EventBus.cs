using System.Text.Json;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace NexUS.BuildingBlocks.Messaging;

/// <summary>Mengirim event ke broker. Hanya dipanggil relay outbox, bukan handler (CONTEXT.md bagian 3 poin 2).</summary>
public interface IEventPublisher
{
    /// <summary>
    /// Selesai setelah broker mengonfirmasi pesan. Melempar <see cref="UnroutableEventException"/> bila belum ada antrean
    /// yang menerima jenis event ini, dan exception lain bila broker tidak terjangkau.
    /// </summary>
    Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken ct = default);
}

/// <summary>Handler satu jenis event. Wajib idempotent: event yang sama bisa datang lebih dari sekali.</summary>
public interface IIntegrationEventHandler<in TEvent> where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken ct);
}

/// <summary>Belum ada antrean yang terikat ke jenis event ini (consumer-nya belum pernah berjalan); event tidak terkirim.</summary>
public sealed class UnroutableEventException(string eventName, Exception inner)
    : Exception($"Belum ada antrean penerima untuk event {eventName}.", inner)
{
    public string EventName { get; } = eventName;
}

public static class EventBusTopology
{
    /// <summary>Satu topic exchange untuk semua event; routing key = nama event (mis. <c>UserCreated</c>).</summary>
    public const string Exchange = "nexus.events";

    /// <summary>Header jumlah percobaan pemrosesan pesan di consumer.</summary>
    public const string AttemptsHeader = "x-nexus-attempts";

    /// <summary>Header error terakhir saat pesan diulang atau dipindah ke dead-letter.</summary>
    public const string LastErrorHeader = "x-nexus-last-error";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string RoutingKey(Type eventType) => eventType.Name;

    /// <summary>Antrean pesan yang gagal diproses berulang kali (dead-letter) milik satu antrean consumer.</summary>
    public static string DeadLetterQueue(string queue) => $"{queue}.dead";

    public static Task DeclareExchangeAsync(IChannel channel, CancellationToken ct) =>
        channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
}

/// <summary>Satu koneksi RabbitMQ per proses, dibuat saat pertama dipakai dan dibuat ulang bila tertutup.</summary>
public sealed class RabbitMqConnection(string? amqpUri, string clientName) : IAsyncDisposable
{
    /// <summary>Batas waktu menutup koneksi/channel saat service berhenti.</summary>
    public static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(5);

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

    public async ValueTask DisposeAsync()
    {
        // Koneksi ke broker yang sempat macet bisa tidak pernah menjawab permintaan tutup; jangan sampai menahan
        // service berhenti tanpa batas.
        if (_connection is { } connection)
            await CloseQuietlyAsync(() => connection.AbortAsync(CloseTimeout), () => connection.DisposeAsync().AsTask());
        _gate.Dispose();
    }

    /// <summary>Menjalankan langkah penutupan berurutan, dibatasi waktu, tanpa meneruskan error (service sedang berhenti).</summary>
    internal static async Task CloseQuietlyAsync(params Func<Task>[] steps)
    {
        async Task RunAsync()
        {
            foreach (var step in steps)
            {
                try { await step(); }
                catch (Exception) { /* service berhenti; kegagalan menutup tidak perlu diteruskan */ }
            }
        }

        await Task.WhenAny(RunAsync(), Task.Delay(CloseTimeout * 2));
    }
}

/// <summary>
/// Publisher dengan satu channel ber-publisher-confirm yang dipakai ulang (dibuat ulang bila tertutup); publish
/// diserialkan. Exchange dideklarasikan sekali per channel.
/// </summary>
public sealed class RabbitMqEventPublisher(RabbitMqConnection connection, ILogger<RabbitMqEventPublisher> logger)
    : IEventPublisher, IAsyncDisposable
{
    /// <summary>Batas waktu satu publish, agar broker yang macet (bukan mati) tidak menahan relay tanpa batas.</summary>
    public static readonly TimeSpan PublishTimeout = TimeSpan.FromSeconds(15);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IChannel? _channel;

    public async Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(PublishTimeout);
        var ct = timeout.Token;

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

        await _gate.WaitAsync(ct);
        try
        {
            var channel = await GetChannelAsync(ct);
            try
            {
                // Publisher confirm + mandatory: selesai setelah broker menerima pesan; melempar bila tidak ada antrean
                // penerima, sehingga outbox hanya ditandai terkirim bila pesan benar-benar sampai ke sebuah antrean.
                await channel.BasicPublishAsync(EventBusTopology.Exchange, EventBusTopology.RoutingKey(type), mandatory: true, properties, body, ct);
            }
            catch (PublishReturnException ex)
            {
                throw new UnroutableEventException(type.Name, ex); // channel tetap bisa dipakai
            }
            catch (Exception)
            {
                await ResetChannelAsync(); // timeout/putus: status confirm channel tidak bisa dipercaya lagi
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }

        logger.LogDebug("Event {EventType} {EventId} terkirim", type.Name, integrationEvent.EventId);
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true } open) return open;
        await ResetChannelAsync();

        var options = new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true);
        var channel = await (await connection.GetAsync(ct)).CreateChannelAsync(options, ct);
        await EventBusTopology.DeclareExchangeAsync(channel, ct);
        return _channel = channel;
    }

    private async Task ResetChannelAsync()
    {
        if (_channel is not { } channel) return;
        _channel = null;
        await RabbitMqConnection.CloseQuietlyAsync(() => channel.DisposeAsync().AsTask());
    }

    public async ValueTask DisposeAsync()
    {
        await ResetChannelAsync();
        _gate.Dispose();
    }
}
