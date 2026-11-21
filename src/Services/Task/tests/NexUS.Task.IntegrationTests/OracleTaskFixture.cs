using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Encodings.Web;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexUS.BuildingBlocks.Auth;
using Oracle.ManagedDataAccess.Client;

namespace NexUS.Tasks.IntegrationTests;

/// <summary>
/// Oracle Free dan RabbitMQ sungguhan (Testcontainers) dengan migrasi <c>db/tsk</c> yang sama seperti Flyway, lalu Task
/// Service berjalan di atasnya. Autentikasi memakai <see cref="TestAuthHandler"/>; permission user (yang di produksi dari
/// User Service) diisi lewat <see cref="Grant"/>; read model proyek diisi lewat <see cref="SeedProjectAsync"/> atau event.
/// </summary>
public sealed class OracleTaskFixture : IAsyncLifetime
{
    public const long TenantA = 1;
    public const long TenantB = 2;
    private const string SchemaPassword = "Tsk_Test_2026";

    private readonly IContainer _oracle = new ContainerBuilder("gvenzl/oracle-free:23-slim-faststart")
        .WithEnvironment("ORACLE_PASSWORD", "Sys_Test_2026")
        .WithEnvironment("APP_USER", "TSK_SCHEMA")
        .WithEnvironment("APP_USER_PASSWORD", SchemaPassword)
        .WithPortBinding(1521, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("DATABASE IS READY TO USE!"))
        .Build();

    private readonly IContainer _rabbit = new ContainerBuilder("rabbitmq:3.13-management-alpine")
        .WithEnvironment("RABBITMQ_DEFAULT_USER", "nexus")
        .WithEnvironment("RABBITMQ_DEFAULT_PASS", "nexus_test")
        .WithPortBinding(5672, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server startup complete"))
        .Build();

    private readonly FakePermissionSource _permissions = new();

    public WebApplicationFactory<Program> Factory { get; private set; } = default!;
    public string ConnectionString { get; private set; } = default!;
    public string RabbitConnectionString => $"amqp://nexus:nexus_test@{_rabbit.Hostname}:{_rabbit.GetMappedPublicPort(5672)}";

    /// <summary>Permission tugas lengkap seperti role MEMBER/PROJECT_MANAGER (scope global).</summary>
    public static readonly string[] Worker = ["project.read", "task.read", "task.create", "task.update", "task.delete"];

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_oracle.StartAsync(), _rabbit.StartAsync());
        ConnectionString =
            $"User Id=TSK_SCHEMA;Password={SchemaPassword};Data Source={_oracle.Hostname}:{_oracle.GetMappedPublicPort(1521)}/FREEPDB1";
        await ApplyMigrationsAsync();

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Development")
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Oracle"] = ConnectionString,
                ["ConnectionStrings:RabbitMq"] = RabbitConnectionString,
                ["Services:User:BaseUrl"] = "http://user.invalid",
            }))
            .ConfigureServices(services =>
            {
                services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                });
                // Pengganti panggilan HTTP ke User Service.
                services.RemoveAll<IPermissionSource>();
                services.AddSingleton<IPermissionSource>(_permissions);
            }));
        _ = Factory.Server;
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null) await Factory.DisposeAsync();
        await _oracle.DisposeAsync();
        await _rabbit.DisposeAsync();
    }

    /// <summary>Permission global seorang user, seperti jawaban User Service.</summary>
    public void Grant(long tenantId, Guid userId, params string[] permissions) =>
        _permissions.Set(tenantId, userId, permissions.Select(code => new GrantedPermission(code, "global", null)).ToList());

    /// <summary>User baru dengan permission <see cref="Worker"/> (atau yang diberikan).</summary>
    public Guid NewUser(long tenantId, params string[] permissions)
    {
        var id = Guid.NewGuid();
        Grant(tenantId, id, permissions.Length == 0 ? Worker : permissions);
        return id;
    }

    public HttpClient ClientAs(long tenantId, Guid userId)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        return client;
    }

    /// <summary>Mengisi read model proyek langsung (pengganti event Project Service); peran huruf besar.</summary>
    public async Task<Guid> SeedProjectAsync(long tenantId, bool archived = false, params (Guid UserId, string Role)[] members)
    {
        var projectId = Guid.NewGuid();
        await ExecuteAsync(
            "INSERT INTO PROJECT_REF (TENANT_ID, PROJECT_ID, CODE, NAME, IS_ARCHIVED, CREATED_AT, CREATED_BY) " +
            "VALUES (:t, :p, 'PRJ', 'Proyek Uji', :a, SYSTIMESTAMP, 0)",
            new OracleParameter("t", tenantId), Raw("p", projectId), new OracleParameter("a", archived ? 1 : 0));
        foreach (var (userId, role) in members)
            await AddMemberAsync(tenantId, projectId, userId, role);
        return projectId;
    }

    public Task AddMemberAsync(long tenantId, Guid projectId, Guid userId, string role) => ExecuteAsync(
        "INSERT INTO PROJECT_MEMBER_REF (TENANT_ID, PROJECT_ID, USER_ID, PROJECT_ROLE, LAST_EVENT_AT, CREATED_AT, CREATED_BY) " +
        "VALUES (:t, :p, :u, :r, SYSTIMESTAMP, SYSTIMESTAMP, 0)",
        new OracleParameter("t", tenantId), Raw("p", projectId), Raw("u", userId), new OracleParameter("r", role));

    public static OracleParameter Raw(string name, Guid value) => new(name, OracleDbType.Raw) { Value = value.ToByteArray() };

    public async Task<object?> ScalarAsync(string sql, params OracleParameter[] parameters)
    {
        await using var connection = new OracleConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.BindByName = true;
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        return await command.ExecuteScalarAsync();
    }

    public async Task ExecuteAsync(string sql, params OracleParameter[] parameters)
    {
        await using var connection = new OracleConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.BindByName = true;
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Menunggu antrean consumer dideklarasikan (consumer berjalan di latar saat host start).</summary>
    public async Task WaitForQueueAsync(string queue)
    {
        var factory = new RabbitMQ.Client.ConnectionFactory { Uri = new Uri(RabbitConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        for (var attempt = 0; attempt < 120; attempt++)
        {
            var channel = await connection.CreateChannelAsync();
            try
            {
                await channel.QueueDeclarePassiveAsync(queue);
                return;
            }
            catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
            {
                // Antrean belum ada; deklarasi pasif menutup channel ini.
            }
            finally
            {
                try { await channel.DisposeAsync(); } catch (Exception) { /* channel sudah tertutup */ }
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"Antrean {queue} tidak muncul.");
    }

    /// <summary>Menjalankan V*.sql di db/tsk berurutan, sama seperti Flyway.</summary>
    private async Task ApplyMigrationsAsync()
    {
        var directory = FindRepoDirectory("db/tsk");
        var files = Directory.GetFiles(directory, "V*__*.sql")
            .OrderBy(f => int.Parse(Path.GetFileName(f)[1..Path.GetFileName(f).IndexOf("__", StringComparison.Ordinal)]));

        foreach (var file in files)
            foreach (var statement in SplitStatements(await File.ReadAllTextAsync(file)))
                await ExecuteAsync(statement);
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

    private sealed class FakePermissionSource : IPermissionSource
    {
        private readonly ConcurrentDictionary<(long, Guid), IReadOnlyList<GrantedPermission>> _granted = new();

        public void Set(long tenantId, Guid userId, IReadOnlyList<GrantedPermission> permissions) => _granted[(tenantId, userId)] = permissions;

        public Task<IReadOnlyList<GrantedPermission>> GetAsync(long tenantId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(_granted.TryGetValue((tenantId, userId), out var permissions) ? permissions : []);
    }
}

/// <summary>Pengganti validasi JWT di tes: identitas dibaca dari header <c>X-Test-*</c>.</summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string TenantHeader = "X-Test-Tenant";
    public const string UserHeader = "X-Test-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(TenantHeader, out var tenant) || !Request.Headers.TryGetValue(UserHeader, out var user))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim> { new("sub", user.ToString()), new("tid", tenant.ToString()) };
        var identity = new ClaimsIdentity(claims, SchemeName, nameType: "sub", roleType: ClaimTypes.Role);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

[CollectionDefinition(Name)]
public sealed class OracleCollection : ICollectionFixture<OracleTaskFixture>
{
    public const string Name = "oracle";
}

/// <summary>Host tanpa database: hanya konfigurasi wajib yang diisi.</summary>
public sealed class TaskApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseSetting("Services:User:BaseUrl", "http://user.invalid");
}
