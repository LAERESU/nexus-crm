using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NexUS.BuildingBlocks.Auth;
using NexUS.BuildingBlocks.Caching;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StackExchange.Redis;

namespace NexUS.BuildingBlocks.Tests.Auth;

public class TokenRevocationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 8, 0, 0, TimeSpan.Zero);
    private readonly IDatabase _db = Substitute.For<IDatabase>();
    private readonly IRedisConnection _redis = Substitute.For<IRedisConnection>();
    private readonly RedisTokenRevocationStore _store;

    /// <summary>
    /// Panggilan StringSetAsync yang diterima (key, kedaluwarsa dalam detik). Diperiksa dari ReceivedCalls karena
    /// StackExchange.Redis 3 punya beberapa overload (TimeSpan/Expiration) yang tidak cocok dengan matcher biasa.
    /// </summary>
    private List<(string Key, double Seconds)> SetCalls() =>
        _db.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IDatabase.StringSetAsync))
            .Select(c =>
            {
                var args = c.GetArguments();
                var expiry = args[2]?.ToString() ?? "";
                var seconds = expiry.StartsWith("EX ", StringComparison.Ordinal) ? double.Parse(expiry[3..])
                    : args[2] is TimeSpan ts ? ts.TotalSeconds : -1;
                return (args[0]!.ToString()!, seconds);
            })
            .ToList();

    public TokenRevocationTests()
    {
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_db);
        _redis.Multiplexer.Returns(multiplexer);
        _store = new RedisTokenRevocationStore(_redis, NullLogger<RedisTokenRevocationStore>.Instance);
    }

    [Fact]
    public async Task Revoke_token_berawalan_tenant_dengan_TTL_sisa_umur_ditambah_clock_skew()
    {
        await _store.RevokeTokenAsync(7, "jti-1", Now.AddMinutes(10), Now);

        SetCalls().Should().Equal(("tnt:7:auth:revoked:jti-1", (TimeSpan.FromMinutes(10) + NexusJwt.ClockSkew).TotalSeconds));
    }

    [Fact]
    public async Task Token_yang_baru_kedaluwarsa_tetapi_masih_dalam_clock_skew_tetap_dicabut()
    {
        await _store.RevokeTokenAsync(7, "jti-1", Now.AddSeconds(-10), Now);

        SetCalls().Should().Equal(("tnt:7:auth:revoked:jti-1", (NexusJwt.ClockSkew - TimeSpan.FromSeconds(10)).TotalSeconds));
    }

    [Fact]
    public async Task Token_di_luar_jendela_clock_skew_diabaikan()
    {
        await _store.RevokeTokenAsync(7, "jti-1", Now - NexusJwt.ClockSkew, Now);

        SetCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Revoke_sesi_berlaku_selama_umur_maksimal_access_token()
    {
        await _store.RevokeSessionAsync(7, "sesi-1");

        SetCalls().Should().Equal(("tnt:7:auth:revoked-sid:sesi-1", (NexusJwt.AccessTokenLifetime + NexusJwt.ClockSkew).TotalSeconds));
    }

    [Fact]
    public async Task IsRevoked_memeriksa_jti_dan_sid_dalam_satu_panggilan()
    {
        _db.KeyExistsAsync(Arg.Any<RedisKey[]>(), Arg.Any<CommandFlags>()).Returns(1);

        (await _store.IsRevokedAsync(7, "jti-1", "sesi-1")).Should().BeTrue();

        await _db.Received(1).KeyExistsAsync(
            Arg.Is<RedisKey[]>(k => k.Length == 2 && k[0] == "tnt:7:auth:revoked:jti-1" && k[1] == "tnt:7:auth:revoked-sid:sesi-1"),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task Redis_tidak_terjangkau_dianggap_belum_dicabut_dan_tidak_melempar()
    {
        _db.KeyExistsAsync(Arg.Any<RedisKey[]>(), Arg.Any<CommandFlags>()).ThrowsAsync(new RedisException("mati"));
        _redis.Multiplexer!.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Throws(new RedisException("mati"));

        (await _store.IsRevokedAsync(7, "jti-1", null)).Should().BeFalse();
        await _store.Invoking(s => s.RevokeTokenAsync(7, "jti-1", Now.AddMinutes(5), Now)).Should().NotThrowAsync();
        await _store.Invoking(s => s.RevokeSessionAsync(7, "sesi")).Should().NotThrowAsync();
    }

    private static ServiceProvider BuildServices(string environment, string? redisConnectionString)
    {
        var settings = new Dictionary<string, string?>();
        if (redisConnectionString is not null) settings["ConnectionStrings:Redis"] = redisConnectionString;
        var hostEnvironment = Substitute.For<IHostEnvironment>();
        hostEnvironment.EnvironmentName.Returns(environment);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return new ServiceCollection()
            .AddSingleton(hostEnvironment)
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddNexusCaching(configuration)
            .AddNexusTokenRevocation()
            .BuildServiceProvider();
    }

    [Fact]
    public async Task Tanpa_Redis_di_Development_cache_kosong_dan_blacklist_fail_open()
    {
        await using var services = BuildServices(Environments.Development, null);

        services.GetRequiredService<ICacheService>().Should().BeOfType<NullCacheService>();
        (await services.GetRequiredService<ITokenRevocationStore>().IsRevokedAsync(1, "jti", null)).Should().BeFalse();
    }

    [Fact]
    public void Tanpa_Redis_di_luar_Development_aplikasi_gagal_start()
    {
        using var services = BuildServices(Environments.Production, null);

        var act = () => services.GetRequiredService<ICacheService>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*ConnectionStrings:Redis*");
    }
}
