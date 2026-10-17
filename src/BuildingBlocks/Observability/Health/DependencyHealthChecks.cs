using Microsoft.Extensions.Diagnostics.HealthChecks;
using Oracle.ManagedDataAccess.Client;
using RabbitMQ.Client;
using StackExchange.Redis;

namespace NexUS.BuildingBlocks.Observability;

/// <summary>
/// Batas waktu setiap pemeriksaan agar /health/ready tidak menggantung saat dependency mati.
/// Hasil tidak membawa exception asli: pesan driver bisa memuat connection string atau kredensial.
/// </summary>
internal static class HealthCheckTimeout
{
    public static readonly TimeSpan Default = TimeSpan.FromSeconds(3);

    public static CancellationTokenSource Create(CancellationToken ct)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(ct);
        source.CancelAfter(Default);
        return source;
    }
}

public sealed class OracleHealthCheck(string connectionString) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            using var timeout = HealthCheckTimeout.Create(ct);
            await using var connection = new OracleConnection(connectionString);
            await connection.OpenAsync(timeout.Token);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM DUAL";
            await command.ExecuteScalarAsync(timeout.Token);
            return HealthCheckResult.Healthy();
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Oracle tidak dapat dijangkau.");
        }
    }
}

public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly Lazy<Task<ConnectionMultiplexer>> _redis;

    public RedisHealthCheck(string connectionString)
    {
        var options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = false; // multiplexer tersambung kembali sendiri saat Redis pulih
        options.ConnectTimeout = (int)HealthCheckTimeout.Default.TotalMilliseconds;
        options.ConnectRetry = 1;

        // Satu multiplexer per proses; membuat yang baru pada setiap probe boros dan bocor saat Redis lambat.
        _redis = new Lazy<Task<ConnectionMultiplexer>>(() => ConnectionMultiplexer.ConnectAsync(options));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var redis = await _redis.Value.WaitAsync(HealthCheckTimeout.Default, ct);
            await redis.GetDatabase().PingAsync().WaitAsync(HealthCheckTimeout.Default, ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Redis tidak dapat dijangkau.");
        }
    }
}

public sealed class RabbitMqHealthCheck(string amqpUri) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            using var timeout = HealthCheckTimeout.Create(ct);
            var factory = new ConnectionFactory
            {
                Uri = new Uri(amqpUri),
                RequestedConnectionTimeout = HealthCheckTimeout.Default,
                AutomaticRecoveryEnabled = false,
            };

            await using var connection = await factory.CreateConnectionAsync(timeout.Token);
            return connection.IsOpen
                ? HealthCheckResult.Healthy()
                : new HealthCheckResult(context.Registration.FailureStatus, "Koneksi RabbitMQ tertutup.");
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "RabbitMQ tidak dapat dijangkau.");
        }
    }
}
