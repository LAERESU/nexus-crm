using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using NexUS.BuildingBlocks.Auth;
using StackExchange.Redis;

namespace NexUS.Auth.IntegrationTests;

[Collection(OracleCollection.Name)]
[Trait("Category", "Oracle")]
public class LogoutEndpointTests(OracleAuthFixture fixture)
{
    private const string CookieName = "nexus_rt";

    private sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn);

    private HttpClient Client() => fixture.Factory.CreateClient(new() { HandleCookies = false });

    private static string? RefreshCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Where(v => v.StartsWith(CookieName + "=", StringComparison.Ordinal))
                .Select(v => Uri.UnescapeDataString(v.Split(';')[0][(CookieName.Length + 1)..]))
                .FirstOrDefault()
            : null;

    private async Task<(string AccessToken, string RefreshToken)> LoginAsync()
    {
        var response = await Client().PostAsJsonAsync("/api/v1/auth/login",
            new { email = OracleAuthFixture.AdminEmail, password = OracleAuthFixture.AdminPassword });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return ((await response.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken, RefreshCookie(response)!);
    }

    private static HttpRequestMessage Post(string path, string? refreshToken, string? accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (refreshToken is not null) request.Headers.Add("Cookie", $"{CookieName}={Uri.EscapeDataString(refreshToken)}");
        if (accessToken is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    [Fact]
    public async Task Logout_204_menghapus_cookie_dan_memasukkan_jti_ke_blacklist_Redis_dengan_TTL_sisa_umur()
    {
        var (accessToken, refreshToken) = await LoginAsync();

        var response = await Client().SendAsync(Post("/api/v1/auth/logout", refreshToken, accessToken));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        RefreshCookie(response).Should().BeEmpty("cookie dihapus");

        var jwt = new JsonWebToken(accessToken);
        await using var redis = await ConnectionMultiplexer.ConnectAsync(fixture.RedisConnectionString);
        var key = CacheTokenRevocationStore.Key(long.Parse(jwt.GetClaim("tid").Value), jwt.Id);
        key.Should().StartWith("tnt:1:auth:revoked:");
        (await redis.GetDatabase().StringGetAsync(key)).ToString().Should().Be("\"1\"");
        var ttl = await redis.GetDatabase().KeyTimeToLiveAsync(key);
        ttl.Should().NotBeNull();
        ttl!.Value.Should().BeGreaterThan(TimeSpan.Zero).And.BeLessThanOrEqualTo(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task Setelah_logout_refresh_token_sesi_itu_tidak_bisa_dipakai()
    {
        var (accessToken, refreshToken) = await LoginAsync();
        await Client().SendAsync(Post("/api/v1/auth/logout", refreshToken, accessToken));

        var refresh = await Client().SendAsync(Post("/api/v1/auth/refresh", refreshToken, null));

        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_hanya_mengakhiri_sesi_itu_bukan_sesi_lain()
    {
        var first = await LoginAsync();
        var second = await LoginAsync();

        await Client().SendAsync(Post("/api/v1/auth/logout", first.RefreshToken, first.AccessToken));

        (await Client().SendAsync(Post("/api/v1/auth/refresh", second.RefreshToken, null)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Logout_tanpa_token_atau_dengan_token_palsu_tetap_204()
    {
        (await Client().SendAsync(Post("/api/v1/auth/logout", null, null))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Client().SendAsync(Post("/api/v1/auth/logout", "1.palsu", "bukan.jwt.valid")))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
