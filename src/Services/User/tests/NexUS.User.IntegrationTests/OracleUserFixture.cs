using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NexUS.User.Domain.Users;
using NexUS.User.Infrastructure.Persistence;
using NexUS.User.Infrastructure.Seeding;
using StackExchange.Redis;
using UserEntity = NexUS.User.Domain.Users.User;
using Oracle.ManagedDataAccess.Client;

namespace NexUS.User.IntegrationTests;

/// <summary>
/// Oracle Free sungguhan (Testcontainers) dengan migrasi <c>db/usr</c> yang sama seperti Flyway, lalu User Service
/// berjalan di atasnya dengan dua tenant, masing-masing punya user admin (TENANT_ADMIN) dan anggota (MEMBER) sungguhan,
/// plus Redis sungguhan untuk cache permission. Autentikasi memakai <see cref="TestAuthHandler"/>; hak akses tetap
/// ditentukan permission di database, bukan header.
/// </summary>
public sealed class OracleUserFixture : IAsyncLifetime
{
    public const long TenantA = 1;
    public const long TenantB = 2;
    private const string SchemaPassword = "Usr_Test_2026";

    private readonly IContainer _oracle = new ContainerBuilder("gvenzl/oracle-free:23-slim-faststart")
        .WithEnvironment("ORACLE_PASSWORD", "Sys_Test_2026")
        .WithEnvironment("APP_USER", "USR_SCHEMA")
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

    public WebApplicationFactory<Program> Factory { get; private set; } = default!;
    public string RabbitConnectionString { get; private set; } = default!;
    public string RedisConnectionString { get; private set; } = default!;
    public IConnectionMultiplexer Redis { get; private set; } = default!;
    public string ConnectionString { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_oracle.StartAsync(), _redis.StartAsync(), _rabbit.StartAsync());
        RabbitConnectionString = $"amqp://nexus:nexus_test@{_rabbit.Hostname}:{_rabbit.GetMappedPublicPort(5672)}";
        await DeclareConsumerQueueAsync();
        RedisConnectionString = $"{_redis.Hostname}:{_redis.GetMappedPublicPort(6379)},abortConnect=false";
        Redis = await ConnectionMultiplexer.ConnectAsync(RedisConnectionString);
        ConnectionString =
            $"User Id=USR_SCHEMA;Password={SchemaPassword};Data Source={_oracle.Hostname}:{_oracle.GetMappedPublicPort(1521)}/FREEPDB1";

        await ApplyMigrationsAsync();
        await ExecuteAsync(
            $"INSERT INTO TENANTS (ID, CODE, NAME, CREATED_AT, CREATED_BY) VALUES ({TenantA}, 'tenant-a', 'Tenant A', SYSTIMESTAMP, 0)",
            $"INSERT INTO TENANTS (ID, CODE, NAME, CREATED_AT, CREATED_BY) VALUES ({TenantB}, 'tenant-b', 'Tenant B', SYSTIMESTAMP, 0)");

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Development")
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Oracle"] = ConnectionString,
                ["ConnectionStrings:Redis"] = RedisConnectionString,
                ["ConnectionStrings:RabbitMq"] = RabbitConnectionString,
            }))
            .ConfigureServices(services =>
            {
                services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                });
            }));
        _ = Factory.Server; // memicu start host (dan seeder role bawaan) sekarang
        await Factory.Services.GetRequiredService<DefaultRoleSeeder>().Completion.WaitAsync(TimeSpan.FromMinutes(2));
        await Factory.Services.GetRequiredService<DevSeeder>().Completion.WaitAsync(TimeSpan.FromMinutes(2));

        foreach (var tenant in new[] { TenantA, TenantB })
        {
            await SeedUserAsync(tenant, AdminId(tenant), "TENANT_ADMIN");
            await SeedUserAsync(tenant, MemberId(tenant), "MEMBER");
        }
    }

    /// <summary>Antrean penerima <c>UserCreated</c> (peran consumer Auth); tanpa antrean, publish mandatory gagal.</summary>
    public const string UserCreatedQueue = "test.UserCreated";

    private async Task DeclareConsumerQueueAsync()
    {
        var factory = new RabbitMQ.Client.ConnectionFactory { Uri = new Uri(RabbitConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await NexUS.BuildingBlocks.Messaging.EventBusTopology.DeclareExchangeAsync(channel, default);
        await channel.QueueDeclareAsync(UserCreatedQueue, durable: true, exclusive: false, autoDelete: false);
        await channel.QueueBindAsync(UserCreatedQueue, NexUS.BuildingBlocks.Messaging.EventBusTopology.Exchange, "UserCreated");
    }

    /// <summary>User admin tetap per tenant; memegang role TENANT_ADMIN secara global.</summary>
    public static Guid AdminId(long tenantId) => Guid.Parse($"a0000000-0000-0000-0000-{tenantId:D12}");

    /// <summary>User anggota tetap per tenant; hanya memegang role MEMBER secara global.</summary>
    public static Guid MemberId(long tenantId) => Guid.Parse($"b0000000-0000-0000-0000-{tenantId:D12}");

    private async Task SeedUserAsync(long tenantId, Guid publicId, string roleCode)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<UserDbContext>();
        var roleId = await db.Roles.IgnoreQueryFilters().Where(r => r.TenantId == tenantId && r.Code == roleCode).Select(r => r.Id).SingleAsync();

        var user = UserEntity.Create(tenantId, $"{roleCode.ToLowerInvariant()}-{tenantId}@uji.local", $"{roleCode} {tenantId}", null, null, publicId);
        user.SetRoles([new RoleAssignment(roleId, RoleScopeType.Global, null)]);
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null) await Factory.DisposeAsync();
        Redis?.Dispose();
        await _oracle.DisposeAsync();
        await _redis.DisposeAsync();
        await _rabbit.DisposeAsync();
    }

    /// <summary>
    /// Klien yang berperan sebagai user tertentu. Tanpa <paramref name="userId"/> berarti admin tenant itu
    /// (<see cref="AdminId"/>); user lain hanya punya akses sebanyak role yang ditugaskan kepadanya.
    /// </summary>
    public HttpClient ClientAs(long tenantId, Guid? userId = null)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, (userId ?? AdminId(tenantId)).ToString());
        return client;
    }

    /// <summary>Klien sebagai anggota biasa (role MEMBER) di tenant itu.</summary>
    public HttpClient MemberClient(long tenantId) => ClientAs(tenantId, MemberId(tenantId));

    public async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new OracleConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private async Task ExecuteAsync(params string[] statements)
    {
        await using var connection = new OracleConnection(ConnectionString);
        await connection.OpenAsync();
        foreach (var statement in statements)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>Menjalankan V*.sql di db/usr berurutan, sama seperti Flyway.</summary>
    private async Task ApplyMigrationsAsync()
    {
        var directory = FindRepoDirectory("db/usr");
        var files = Directory.GetFiles(directory, "V*__*.sql")
            .OrderBy(f => int.Parse(Path.GetFileName(f)[1..Path.GetFileName(f).IndexOf("__", StringComparison.Ordinal)]));

        foreach (var file in files)
            await ExecuteAsync(SplitStatements(await File.ReadAllTextAsync(file)).ToArray());
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
public sealed class OracleCollection : ICollectionFixture<OracleUserFixture>
{
    public const string Name = "oracle";
}
