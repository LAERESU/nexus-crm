using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NexUS.BuildingBlocks.Caching;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StackExchange.Redis;

namespace NexUS.BuildingBlocks.Tests.Caching;

public class RedisCacheServiceTests
{
    private readonly IConnectionMultiplexer _redis = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _db = Substitute.For<IDatabase>();
    private readonly RedisCacheService _sut;

    public RedisCacheServiceTests()
    {
        _redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_db);
        _sut = new RedisCacheService(
            _redis,
            Options.Create(new CacheOptions { KeyPrefix = "tsk:", DefaultTtlMinutes = 5 }),
            NullLogger<RedisCacheService>.Instance);
    }

    private void RedisDown() =>
        _redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>())
            .Throws(new RedisException("Redis mati"));

    [Fact]
    public async Task Get_mengembalikan_nilai_dengan_prefix_key()
    {
        _db.StringGetAsync("tsk:k", Arg.Any<CommandFlags>()).Returns((RedisValue)"\"halo\"");

        var value = await _sut.GetAsync<string>("k");

        value.Should().Be("halo");
    }

    [Fact]
    public async Task Get_mengembalikan_default_bila_key_tidak_ada()
    {
        _db.StringGetAsync("tsk:k", Arg.Any<CommandFlags>()).Returns(RedisValue.Null);

        (await _sut.GetAsync<string>("k")).Should().BeNull();
    }

    [Fact]
    public async Task Get_mengembalikan_default_bukan_exception_bila_redis_tidak_tersedia()
    {
        RedisDown();

        var value = await _sut.GetAsync<string>("k");

        value.Should().BeNull();
    }

    [Fact]
    public async Task Set_dan_Remove_tidak_melempar_exception_bila_redis_tidak_tersedia()
    {
        RedisDown();

        await _sut.Invoking(s => s.SetAsync("k", "v")).Should().NotThrowAsync();
        await _sut.Invoking(s => s.RemoveAsync("k")).Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetOrSet_memanggil_factory_bila_redis_tidak_tersedia()
    {
        RedisDown();

        var value = await _sut.GetOrSetAsync("k", _ => Task.FromResult("dari-db"));

        value.Should().Be("dari-db");
    }

    [Fact]
    public async Task GetOrSet_tidak_memanggil_factory_bila_cache_hit()
    {
        _db.StringGetAsync("tsk:k", Arg.Any<CommandFlags>()).Returns((RedisValue)"\"cache\"");
        var dipanggil = false;

        var value = await _sut.GetOrSetAsync("k", _ =>
        {
            dipanggil = true;
            return Task.FromResult("db");
        });

        value.Should().Be("cache");
        dipanggil.Should().BeFalse();
    }

    [Fact]
    public void CacheKeys_Tenant_diawali_tnt_tenantId() =>
        CacheKeys.Tenant(7, "projects:1").Should().Be("tnt:7:projects:1");
}
