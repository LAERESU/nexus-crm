using System.Text.Json;

namespace NexUS.BuildingBlocks.Messaging;

/// <summary>
/// Dasar <see cref="IOutboxEventMapper"/>: daftarkan setiap domain event yang punya kontrak beserta fungsi pemetaannya.
/// ID dan waktu kontrak diambil dari baris outbox (nilai di record domain dibuat ulang saat deserialisasi).
/// </summary>
public abstract class OutboxEventMapper : IOutboxEventMapper
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Dictionary<string, Func<OutboxMessage, IIntegrationEvent>> _maps = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> PublishedTypes => _maps.Keys;

    /// <summary>Mendaftarkan pemetaan domain event <typeparamref name="TDomain"/> ke kontrak.</summary>
    protected void Map<TDomain>(Func<TDomain, Guid, DateTimeOffset, IIntegrationEvent> toContract) =>
        _maps[typeof(TDomain).FullName!] = message =>
            toContract(
                JsonSerializer.Deserialize<TDomain>(message.Payload, Json) ?? throw new JsonException($"Payload {typeof(TDomain).Name} kosong."),
                message.Id,
                message.OccurredAt);

    public IIntegrationEvent? Map(OutboxMessage message)
    {
        // TYPE berisi nama tipe lengkap diikuti nama assembly.
        var comma = message.Type.IndexOf(',', StringComparison.Ordinal);
        var typeName = comma < 0 ? message.Type : message.Type[..comma];
        return _maps.TryGetValue(typeName, out var map) ? map(message) : null;
    }
}
