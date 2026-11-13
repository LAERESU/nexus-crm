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

namespace NexUS.Project.IntegrationTests;

/// <summary>
/// Oracle Free sungguhan (Testcontainers) dengan migrasi <c>db/prj</c> yang sama seperti Flyway, lalu Project Service
/// berjalan di atasnya. Autentikasi memakai <see cref="TestAuthHandler"/>, dan permission user (yang di produksi
/// diambil dari User Service) diisi lewat <see cref="Grant"/>.
/// </summary>
public sealed class OracleProjectFixture : IAsyncLifetime
{
    public const long TenantA = 1;
    public const long TenantB = 2;
    private const string SchemaPassword = "Prj_Test_2026";

    private readonly IContainer _oracle = new ContainerBuilder("gvenzl/oracle-free:23-slim-faststart")
        .WithEnvironment("ORACLE_PASSWORD", "Sys_Test_2026")
        .WithEnvironment("APP_USER", "PRJ_SCHEMA")
        .WithEnvironment("APP_USER_PASSWORD", SchemaPassword)
        .WithPortBinding(1521, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("DATABASE IS READY TO USE!"))
        .Build();

    private readonly FakePermissionSource _permissions = new();

    public WebApplicationFactory<Program> Factory { get; private set; } = default!;
    public string ConnectionString { get; private set; } = default!;

    /// <summary>Manajer proyek tetap per tenant: boleh membaca, membuat, dan mengubah proyek.</summary>
    public static Guid ManagerId(long tenantId) => Guid.Parse($"a0000000-0000-0000-0000-{tenantId:D12}");

    /// <summary>Anggota tetap per tenant: hanya boleh membaca proyek.</summary>
    public static Guid MemberId(long tenantId) => Guid.Parse($"b0000000-0000-0000-0000-{tenantId:D12}");

    public async Task InitializeAsync()
    {
        await _oracle.StartAsync();
        ConnectionString =
            $"User Id=PRJ_SCHEMA;Password={SchemaPassword};Data Source={_oracle.Hostname}:{_oracle.GetMappedPublicPort(1521)}/FREEPDB1";
        await ApplyMigrationsAsync();

        foreach (var tenant in new[] { TenantA, TenantB })
        {
            Grant(tenant, ManagerId(tenant), "project.read", "project.create", "project.update");
            Grant(tenant, MemberId(tenant), "project.read");
        }

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Development")
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Oracle"] = ConnectionString,
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
    }

    /// <summary>Menetapkan permission (scope global) seorang user, seperti jawaban User Service.</summary>
    public void Grant(long tenantId, Guid userId, params string[] permissions) => _permissions.Set(tenantId, userId, permissions);

    /// <summary>Klien sebagai user tertentu; tanpa <paramref name="userId"/> berarti manajer proyek tenant itu.</summary>
    public HttpClient ClientAs(long tenantId, Guid? userId = null)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, (userId ?? ManagerId(tenantId)).ToString());
        return client;
    }

    public HttpClient MemberClient(long tenantId) => ClientAs(tenantId, MemberId(tenantId));

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

    /// <summary>Menjalankan V*.sql di db/prj berurutan, sama seperti Flyway.</summary>
    private async Task ApplyMigrationsAsync()
    {
        var directory = FindRepoDirectory("db/prj");
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

    private sealed class FakePermissionSource : IPermissionSource
    {
        private readonly ConcurrentDictionary<(long, Guid), IReadOnlyList<GrantedPermission>> _granted = new();

        public void Set(long tenantId, Guid userId, string[] permissions) =>
            _granted[(tenantId, userId)] = permissions.Select(code => new GrantedPermission(code, "global", null)).ToList();

        public Task<IReadOnlyList<GrantedPermission>> GetAsync(long tenantId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(_granted.TryGetValue((tenantId, userId), out var permissions) ? permissions : []);
    }
}

/// <summary>
/// Pengganti validasi JWT di tes: identitas dibaca dari header <c>X-Test-*</c>, sehingga setiap request bisa
/// berperan sebagai tenant/user yang berbeda tanpa menjalankan Auth Service.
/// </summary>
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
public sealed class OracleCollection : ICollectionFixture<OracleProjectFixture>
{
    public const string Name = "oracle";
}
