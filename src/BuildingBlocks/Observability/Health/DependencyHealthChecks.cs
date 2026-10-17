using Microsoft.Extensions.Diagnostics.HealthChecks;
using Oracle.ManagedDataAccess.Client;
using RabbitMQ.Client;
using StackExchange.Redis;

namespace NexUS.BuildingBlocks.Observability;

/// <summary>Batas waktu setiap pemeriksaan agar /health/ready tidak menggantung saat dependency mati.</summary>
internal static class HealthCheckTimeout
{
    public static readonly TimeSpan Default = TimeSpan.FromSeconds(3);
}

public sealed class OracleHealthCheck(string connectionString) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(HealthCheckTimeout.Default);

            await using var connection = new OracleConnection(connectionString);
            await connection.OpenAsync(timeout.Token).WaitAsync(HealthCheckTimeout.Default, ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM DUAL";
            await command.ExecuteScalarAsync(timeout.Token).WaitAsync(HealthCheckTimeout.Default, ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("Oracle tidak dapat dijangkau.", ex);
        }
    }
}

public sealed class RedisHealthCheck(string connectionString) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var options = ConfigurationOptions.Parse(connectionString);
            options.AbortOnConnectFail = true;
            options.ConnectTimeout = (int)HealthCheckTimeout.Default.TotalMilliseconds;
            options.ConnectRetry = 1;

            await using var redis = await ConnectionMultiplexer.ConnectAsync(options).WaitAsync(HealthCheckTimeout.Default, ct);
            await redis.GetDatabase().PingAsync().WaitAsync(HealthCheckTimeout.Default, ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("Redis tidak dapat dijangkau.", ex);
        }
    }
}

public sealed class RabbitMqHealthCheck(string amqpUri) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var factory = new ConnectionFactory
            {
                Uri = new Uri(amqpUri),
                RequestedConnectionTimeout = HealthCheckTimeout.Default,
                AutomaticRecoveryEnabled = false,
            };

            await using var connection = await factory.CreateConnectionAsync(ct).WaitAsync(HealthCheckTimeout.Default, ct);
            return connection.IsOpen ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Koneksi RabbitMQ tertutup.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ tidak dapat dijangkau.", ex);
        }
    }
}
