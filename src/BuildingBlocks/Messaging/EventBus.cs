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

    public static Task DeclareExchangeAsync(IChannel channel, CancellationToken ct) =>
        channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
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

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
        _gate.Dispose();
    }
}

public sealed class RabbitMqEventPublisher(RabbitMqConnection connection, ILogger<RabbitMqEventPublisher> logger) : IEventPublisher
{
    public async Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken ct = default)
    {
        // Publisher confirm + mandatory: BasicPublishAsync baru selesai setelah broker menerima pesan dan melempar
        // exception bila tidak ada antrean yang menerimanya (consumer belum pernah berjalan), sehingga outbox hanya
        // ditandai terkirim bila pesan benar-benar sampai ke sebuah antrean.
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
