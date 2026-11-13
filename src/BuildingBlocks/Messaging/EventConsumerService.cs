using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NexUS.BuildingBlocks.Messaging;

/// <summary>
/// Mengonsumsi satu jenis event dari antrean durable milik service (<c>{queuePrefix}.{NamaEvent}</c>). Pesan baru di-ack
/// setelah handler selesai; bila handler gagal, pesan dikembalikan ke antrean setelah jeda. Pesan yang tidak bisa dibaca
/// dibuang (dicatat sebagai error) agar tidak berputar selamanya.
/// </summary>
public sealed class EventConsumerService<TEvent>(
    RabbitMqConnection connection,
    IServiceScopeFactory scopes,
    string queuePrefix,
    ILogger<EventConsumerService<TEvent>> logger) : BackgroundService where TEvent : class, IIntegrationEvent
{
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
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
            logger.LogError(ex, "Pesan {MessageId} di {Queue} tidak dapat dibaca; dibuang", delivery.BasicProperties.MessageId, QueueName);
            await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, ct);
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
            logger.LogError(ex, "Event {EventId} gagal diproses di {Queue}; dicoba lagi dalam {Delay}", integrationEvent.EventId, QueueName, RetryDelay);
            try
            {
                await Task.Delay(RetryDelay, ct);
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, ct);
            }
            catch (Exception nackFailure) when (nackFailure is not OperationCanceledException)
            {
                logger.LogWarning(nackFailure, "Nack gagal di {Queue}; broker mengirim ulang setelah channel tertutup", QueueName);
            }
        }
    }
}
