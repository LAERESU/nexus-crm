using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace NexUS.BuildingBlocks.Messaging;

public static class MessagingServiceCollectionExtensions
{
    public const string ConnectionStringName = "RabbitMq";

    /// <summary>
    /// Connection string dibaca saat koneksi dibuat (bukan saat pendaftaran), agar konfigurasi yang ditambahkan
    /// belakangan (environment, tes) ikut terbaca. Bila kosong, relay dan consumer berhenti sendiri saat start.
    /// </summary>
    private static void AddConnection(IServiceCollection services, string clientName)
    {
        services.TryAddSingleton(sp => new RabbitMqConnection(
            sp.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName), clientName));
        services.TryAddSingleton<IEventPublisher, RabbitMqEventPublisher>();
    }

    /// <summary>Menjalankan relay outbox untuk <typeparamref name="TContext"/> dengan pemetaan event <typeparamref name="TMapper"/>.</summary>
    public static IServiceCollection AddNexusOutboxPublisher<TContext, TMapper>(this IServiceCollection services, string serviceName)
        where TContext : DbContext
        where TMapper : class, IOutboxEventMapper
    {
        services.TryAddSingleton<IOutboxEventMapper, TMapper>();
        AddConnection(services, serviceName);

        services.AddSingleton<OutboxPublisherWorker<TContext>>();
        services.AddHostedService(sp => sp.GetRequiredService<OutboxPublisherWorker<TContext>>());
        return services;
    }

    /// <summary>Mendaftarkan handler event dan consumer antrean <c>{serviceName}.{NamaEvent}</c>.</summary>
    public static IServiceCollection AddNexusEventConsumer<TEvent, THandler>(this IServiceCollection services, string serviceName)
        where TEvent : class, IIntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        services.AddScoped<IIntegrationEventHandler<TEvent>, THandler>();
        AddConnection(services, serviceName);

        services.AddHostedService(sp => new EventConsumerService<TEvent>(
            sp.GetRequiredService<RabbitMqConnection>(),
            sp.GetRequiredService<IServiceScopeFactory>(),
            serviceName,
            sp.GetRequiredService<ILogger<EventConsumerService<TEvent>>>()));
        return services;
    }
}
