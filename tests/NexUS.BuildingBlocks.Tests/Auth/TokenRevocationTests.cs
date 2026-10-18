using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexUS.BuildingBlocks.Auth;
using NexUS.BuildingBlocks.Caching;
using NSubstitute;

namespace NexUS.BuildingBlocks.Tests.Auth;

public class TokenRevocationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 8, 0, 0, TimeSpan.Zero);
    private readonly ICacheService _cache = Substitute.For<ICacheService>();

    [Fact]
    public async Task Revoke_menyimpan_jti_dengan_awalan_tenant_dan_TTL_sisa_umur_token()
    {
        var store = new CacheTokenRevocationStore(_cache);

        await store.RevokeAsync(7, "jti-1", Now.AddMinutes(10), Now);

        await _cache.Received(1).SetAsync("tnt:7:auth:revoked:jti-1", "1", TimeSpan.FromMinutes(10), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Token_yang_sudah_kedaluwarsa_tidak_disimpan()
    {
        var store = new CacheTokenRevocationStore(_cache);

        await store.RevokeAsync(7, "jti-1", Now.AddSeconds(-1), Now);
        await store.RevokeAsync(7, "jti-2", Now, Now);

        await _cache.DidNotReceiveWithAnyArgs().SetAsync<string>(default!, default!, default, default);
    }

    [Fact]
    public async Task IsRevoked_true_hanya_bila_penanda_ada_di_tenant_yang_sama()
    {
        _cache.GetAsync<string>("tnt:7:auth:revoked:jti-1", Arg.Any<CancellationToken>()).Returns("1");
        var store = new CacheTokenRevocationStore(_cache);

        (await store.IsRevokedAsync(7, "jti-1")).Should().BeTrue();
        (await store.IsRevokedAsync(8, "jti-1")).Should().BeFalse();
        (await store.IsRevokedAsync(7, "jti-lain")).Should().BeFalse();
        (await store.IsRevokedAsync(7, "")).Should().BeFalse();
    }

    [Fact]
    public async Task Tanpa_connection_string_Redis_aplikasi_memakai_NullCacheService()
    {
        var services = new ServiceCollection()
            .AddNexusCaching(new ConfigurationBuilder().Build())
            .AddNexusTokenRevocation()
            .BuildServiceProvider();

        services.GetRequiredService<ICacheService>().Should().BeOfType<NullCacheService>();
        var store = services.GetRequiredService<ITokenRevocationStore>();
        await store.RevokeAsync(1, "jti", Now.AddMinutes(5), Now);
        (await store.IsRevokedAsync(1, "jti")).Should().BeFalse("tanpa Redis, blacklist tidak tersedia (fail open)");
    }
}
