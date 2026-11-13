using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexUS.Auth.Infrastructure.Seeding;
using Oracle.ManagedDataAccess.Client;

namespace NexUS.Auth.IntegrationTests;

/// <summary>
/// Oracle Free, Redis, RabbitMQ, dan Mailpit sungguhan (Testcontainers) dengan migrasi <c>db/auth</c> yang sama seperti Flyway,
/// lalu Auth Service berjalan di atasnya dengan seed admin dev.
/// </summary>
public sealed class OracleAuthFixture : IAsyncLifetime
{
    public const string AdminEmail = "admin@demo.local";
    public const string AdminPassword = "Admin#Test2026";
    private const string SchemaPassword = "Auth_Test_2026";

    private readonly IContainer _oracle = new ContainerBuilder("gvenzl/oracle-free:23-slim-faststart")
        .WithEnvironment("ORACLE_PASSWORD", "Sys_Test_2026")
        .WithEnvironment("APP_USER", "AUTH_SCHEMA")
        .WithEnvironment("APP_USER_PASSWORD", SchemaPassword)
        .WithPortBinding(1521, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("DATABASE IS READY TO USE!"))
        .Build();

    private readonly IContainer _redis = new ContainerBuilder("redis:7-alpine")
        .WithPortBinding(6379, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Ready to accept connections"))
        .Build();

    private readonly IContainer _rabbit = new ContainerBuilder("rabbitmq:3.13-management-alpine")
        .WithEnvironment("RABBITMQ_DEFAULT_USER", "nexus")
        .WithEnvironment("RABBITMQ_DEFAULT_PASS", "nexus_test")
        .WithPortBinding(5672, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server startup complete"))
        .Build();

    private readonly IContainer _mailpit = new ContainerBuilder("axllent/mailpit:latest")
        .WithPortBinding(1025, assignRandomHostPort: true)
        .WithPortBinding(8025, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8025).ForPath("/readyz")))
        .Build();

    public WebApplicationFactory<Program> Factory { get; private set; } = default!;

    /// <summary>API HTTP Mailpit untuk membaca email yang dikirim Auth Service.</summary>
    public HttpClient Mailpit { get; private set; } = default!;
    public string ConnectionString { get; private set; } = default!;
    public string RedisConnectionString { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_oracle.StartAsync(), _redis.StartAsync(), _rabbit.StartAsync(), _mailpit.StartAsync());
        Mailpit = new HttpClient { BaseAddress = new Uri($"http://{_mailpit.Hostname}:{_mailpit.GetMappedPublicPort(8025)}/") };
        RedisConnectionString = $"{_redis.Hostname}:{_redis.GetMappedPublicPort(6379)},abortConnect=false";
        ConnectionString =
            $"User Id=AUTH_SCHEMA;Password={SchemaPassword};Data Source={_oracle.Hostname}:{_oracle.GetMappedPublicPort(1521)}/FREEPDB1";

        await ApplyMigrationsAsync();

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Development") // seed admin dev hanya berjalan di Development
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Oracle"] = ConnectionString,
                ["ConnectionStrings:Redis"] = RedisConnectionString,
                ["Seed:AdminPassword"] = AdminPassword,
                ["ConnectionStrings:RabbitMq"] = $"amqp://nexus:nexus_test@{_rabbit.Hostname}:{_rabbit.GetMappedPublicPort(5672)}",
                ["Email:SmtpHost"] = _mailpit.Hostname,
                ["Email:SmtpPort"] = _mailpit.GetMappedPublicPort(1025).ToString(),
                ["Email:ActivationUrlTemplate"] = "http://localhost:5173/activate?token={token}",
            })));
        _ = Factory.Server; // memicu start host (dan seeder) sekarang
        await Factory.Services.GetRequiredService<DevSeeder>().Completion.WaitAsync(TimeSpan.FromMinutes(2));
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null) await Factory.DisposeAsync();
        Mailpit?.Dispose();
        await _oracle.DisposeAsync();
        await _redis.DisposeAsync();
        await _rabbit.DisposeAsync();
        await _mailpit.DisposeAsync();
    }

    /// <summary>Menjalankan V*.sql di db/auth berurutan, sama seperti Flyway.</summary>
    private async Task ApplyMigrationsAsync()
    {
        var directory = FindRepoDirectory("db/auth");
        var files = Directory.GetFiles(directory, "V*__*.sql")
            .OrderBy(f => int.Parse(Path.GetFileName(f)[1..Path.GetFileName(f).IndexOf("__", StringComparison.Ordinal)]));

        await using var connection = new OracleConnection(ConnectionString);
        await connection.OpenAsync();
        foreach (var file in files)
        {
            foreach (var statement in SplitStatements(await File.ReadAllTextAsync(file)))
            {
                await using var command = connection.CreateCommand();
                command.CommandText = statement;
                await command.ExecuteNonQueryAsync();
            }
        }
    }

    private static IEnumerable<string> SplitStatements(string sql)
    {
        var withoutComments = string.Join('\n', sql.Split('\n').Select(line =>
        {
            var index = line.IndexOf("--", StringComparison.Ordinal);
            return index >= 0 ? line[..index] : line;
        }));
        return withoutComments.Split(';').Select(s => s.Trim()).Where(s => s.Length > 0);
    }

    private static string FindRepoDirectory(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (Directory.Exists(candidate)) return candidate;
        }

        throw new DirectoryNotFoundException($"Folder {relative} tidak ditemukan dari {AppContext.BaseDirectory}");
    }
}

[CollectionDefinition(Name)]
public sealed class OracleCollection : ICollectionFixture<OracleAuthFixture>
{
    public const string Name = "oracle";
}
