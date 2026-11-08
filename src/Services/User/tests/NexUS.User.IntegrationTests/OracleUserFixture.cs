using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexUS.User.Infrastructure.Seeding;
using Oracle.ManagedDataAccess.Client;

namespace NexUS.User.IntegrationTests;

/// <summary>
/// Oracle Free sungguhan (Testcontainers) dengan migrasi <c>db/usr</c> yang sama seperti Flyway, lalu User Service
/// berjalan di atasnya dengan dua tenant. Autentikasi memakai <see cref="TestAuthHandler"/>.
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

    public WebApplicationFactory<Program> Factory { get; private set; } = default!;
    public string ConnectionString { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        await _oracle.StartAsync();
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
                ["ConnectionStrings:Redis"] = "localhost:6379,abortConnect=false,connectTimeout=100", // tidak dipakai endpoint User
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
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null) await Factory.DisposeAsync();
        await _oracle.DisposeAsync();
    }

    /// <summary>Klien yang berperan sebagai user tertentu; role <c>admin</c> diperlukan untuk endpoint kelola.</summary>
    public HttpClient ClientAs(long tenantId, Guid? userId = null, string roles = "admin")
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, (userId ?? Guid.NewGuid()).ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, roles);
        return client;
    }

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
