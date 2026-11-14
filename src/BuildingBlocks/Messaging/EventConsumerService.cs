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
/// percobaan di header (jeda bertambah dua kali lipat sampai batas); setelah <see cref="EventConsumerOptions.MaxAttempts"/>
/// kali, atau bila pesan tidak bisa dibaca, pesan dipindah ke
/// antrean dead-letter <c>{antrean}.dead</c> agar tidak berputar selamanya dan tetap bisa diperiksa (ADR-0012).
/// </summary>
public sealed class EventConsumerService<TEvent>(
    RabbitMqConnection connection,
    IServiceScopeFactory scopes,
    string queuePrefix,
    EventConsumerOptions options,
    ILogger<EventConsumerService<TEvent>> logger) : BackgroundService where TEvent : class, IIntegrationEvent
{
    /// <summary>Jeda menyambung ulang ke broker.</summary>
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
        // Publisher confirm: salinan pesan (ulang/dead-letter) dipastikan diterima broker sebelum aslinya di-ack.
        var channelOptions = new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true);
        await using var channel = await (await connection.GetAsync(ct)).CreateChannelAsync(channelOptions, ct);
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
            if (attempts >= options.MaxAttempts)
            {
                logger.LogError(ex, "Event {EventId} gagal diproses {Attempts} kali di {Queue}; dipindah ke dead-letter",
                    integrationEvent.EventId, attempts, QueueName);
                await ForwardAsync(channel, delivery, EventBusTopology.DeadLetterQueue(QueueName), attempts, ex.Message, ct);
                return;
            }

            var delay = options.DelayFor(attempts);
            logger.LogWarning(ex, "Event {EventId} gagal diproses di {Queue} (percobaan {Attempts}); dicoba lagi dalam {Delay}",
                integrationEvent.EventId, QueueName, attempts, delay);
            try { await Task.Delay(delay, ct); }
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
                [EventBusTopology.LastErrorHeader] = error.Length > 500 ? error[..500] : error,
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

/// <summary>
/// Pengulangan consumer (bagian konfigurasi <c>Messaging:Consumer</c>). Bawaan: jeda 5 detik berlipat dua sampai
/// 60 detik, 20 percobaan (sekitar 16 menit), agar gangguan sementara (database, SMTP) tidak membuang event ke dead-letter.
/// </summary>
public sealed class EventConsumerOptions
{
    public const string SectionName = "Messaging:Consumer";

    public int MaxAttempts { get; set; } = 20;
    public int RetryDelaySeconds { get; set; } = 5;
    public int MaxRetryDelaySeconds { get; set; } = 60;

    /// <summary>Membaca bagian <see cref="SectionName"/>; nilai yang kosong atau tidak valid memakai bawaan.</summary>
    public static void Read(EventConsumerOptions options, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        if (int.TryParse(section[nameof(MaxAttempts)], out var attempts) && attempts > 0) options.MaxAttempts = attempts;
        if (int.TryParse(section[nameof(RetryDelaySeconds)], out var delay) && delay > 0) options.RetryDelaySeconds = delay;
        if (int.TryParse(section[nameof(MaxRetryDelaySeconds)], out var max) && max > 0) options.MaxRetryDelaySeconds = max;
    }

    /// <summary>Jeda sebelum percobaan berikutnya setelah <paramref name="attempts"/> kali gagal.</summary>
    public TimeSpan DelayFor(int attempts)
    {
        var seconds = RetryDelaySeconds * Math.Pow(2, Math.Clamp(attempts - 1, 0, 16));
        return TimeSpan.FromSeconds(Math.Min(seconds, Math.Max(RetryDelaySeconds, MaxRetryDelaySeconds)));
    }
}
