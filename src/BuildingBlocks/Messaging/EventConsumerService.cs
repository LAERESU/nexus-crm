using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NexUS.BuildingBlocks.Messaging;

/// <summary>
/// Mengonsumsi satu jenis event dari antrean durable milik service (<c>{queuePrefix}.{NamaEvent}</c>). Pesan baru di-ack
/// setelah handler selesai. Bila handler gagal, pesan dikirim ulang ke antrean yang sama setelah jeda dengan jumlah
/// percobaan di header; setelah <see cref="MaxAttempts"/> kali, atau bila pesan tidak bisa dibaca, pesan dipindah ke
/// antrean dead-letter <c>{antrean}.dead</c> agar tidak berputar selamanya dan tetap bisa diperiksa (ADR-0012).
/// </summary>
public sealed class EventConsumerService<TEvent>(
    RabbitMqConnection connection,
    IServiceScopeFactory scopes,
    string queuePrefix,
    ILogger<EventConsumerService<TEvent>> logger) : BackgroundService where TEvent : class, IIntegrationEvent
{
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    public const int MaxAttempts = 5;
    private const ushort Prefetch = 10;

    public string QueueName { get; } = $"{queuePrefix}.{EventBusTopology.RoutingKey(typeof(TEvent))}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!connection.IsConfigured)
        {
            logger.LogWarning("ConnectionStrings:RabbitMq kosong; consumer {Queue} tidak berjalan", QueueName);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Consumer {Queue} terputus; menyambung lagi dalam {Delay}", QueueName, RetryDelay);
            }

            try { await Task.Delay(RetryDelay, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task ConsumeAsync(CancellationToken ct)
    {
        await using var channel = await (await connection.GetAsync(ct)).CreateChannelAsync(cancellationToken: ct);
        await EventBusTopology.DeclareExchangeAsync(channel, ct);
        await channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueDeclareAsync(
            EventBusTopology.DeadLetterQueue(QueueName), durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync(QueueName, EventBusTopology.Exchange, EventBusTopology.RoutingKey(typeof(TEvent)), cancellationToken: ct);
        await channel.BasicQosAsync(0, Prefetch, global: false, ct);

        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.ChannelShutdownAsync += (_, _) =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => HandleDeliveryAsync(channel, delivery, ct);
        await channel.BasicConsumeAsync(QueueName, autoAck: false, consumer, ct);
        logger.LogInformation("Consumer {Queue} siap", QueueName);

        await closed.Task.WaitAsync(ct); // kembali ke loop luar bila channel/koneksi tertutup
    }

    private async Task HandleDeliveryAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken ct)
    {
        TEvent? integrationEvent;
        try
        {
            integrationEvent = JsonSerializer.Deserialize<TEvent>(delivery.Body.Span, EventBusTopology.Json);
            if (integrationEvent is null || integrationEvent.EventId == Guid.Empty) throw new JsonException("Event kosong atau tanpa EventId.");
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Pesan {MessageId} di {Queue} tidak dapat dibaca; dipindah ke dead-letter", delivery.BasicProperties.MessageId, QueueName);
            await ForwardAsync(channel, delivery, EventBusTopology.DeadLetterQueue(QueueName), Attempts(delivery), ex.Message, ct);
            return;
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<TEvent>>().HandleAsync(integrationEvent, ct);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Service berhenti: pesan yang belum di-ack dikirim ulang oleh broker.
        }
        catch (Exception ex)
        {
            var attempts = Attempts(delivery) + 1;
            if (attempts >= MaxAttempts)
            {
                logger.LogError(ex, "Event {EventId} gagal diproses {Attempts} kali di {Queue}; dipindah ke dead-letter",
                    integrationEvent.EventId, attempts, QueueName);
                await ForwardAsync(channel, delivery, EventBusTopology.DeadLetterQueue(QueueName), attempts, ex.Message, ct);
                return;
            }

            logger.LogWarning(ex, "Event {EventId} gagal diproses di {Queue} (percobaan {Attempts}); dicoba lagi dalam {Delay}",
                integrationEvent.EventId, QueueName, attempts, RetryDelay);
            try { await Task.Delay(RetryDelay, ct); }
            catch (OperationCanceledException) { return; } // belum di-ack: broker mengirim ulang
            await ForwardAsync(channel, delivery, QueueName, attempts, ex.Message, ct);
        }
    }

    private static int Attempts(BasicDeliverEventArgs delivery) =>
        delivery.BasicProperties.Headers?.TryGetValue(EventBusTopology.AttemptsHeader, out var value) == true && value is not null
            ? Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture)
            : 0;

    /// <summary>
    /// Mengirim salinan pesan ke <paramref name="queue"/> (lewat default exchange) dengan jumlah percobaan dan error terakhir,
    /// lalu meng-ack aslinya. Bila pengiriman gagal, pesan asli dikembalikan ke antrean (tidak hilang).
    /// </summary>
    private async Task ForwardAsync(IChannel channel, BasicDeliverEventArgs delivery, string queue, int attempts, string error, CancellationToken ct)
    {
        try
        {
            var headers = new Dictionary<string, object?>(delivery.BasicProperties.Headers ?? new Dictionary<string, object?>())
            {
                [EventBusTopology.AttemptsHeader] = attempts,
                ["x-nexus-last-error"] = error.Length > 500 ? error[..500] : error,
            };
            var properties = new BasicProperties
            {
                MessageId = delivery.BasicProperties.MessageId,
                Type = delivery.BasicProperties.Type,
                ContentType = delivery.BasicProperties.ContentType,
                Timestamp = delivery.BasicProperties.Timestamp,
                Persistent = true,
                Headers = headers,
            };
            await channel.BasicPublishAsync(string.Empty, queue, mandatory: false, properties, delivery.Body, ct);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct);
        }
        catch (Exception forwardFailure) when (forwardFailure is not OperationCanceledException)
        {
            logger.LogWarning(forwardFailure, "Pesan {MessageId} tidak dapat dipindah ke {Target}; dikembalikan ke {Queue}",
                delivery.BasicProperties.MessageId, queue, QueueName);
            try { await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, ct); }
            catch (Exception nackFailure) when (nackFailure is not OperationCanceledException)
            {
                logger.LogWarning(nackFailure, "Nack gagal di {Queue}; broker mengirim ulang setelah channel tertutup", QueueName);
            }
        }
    }
}
