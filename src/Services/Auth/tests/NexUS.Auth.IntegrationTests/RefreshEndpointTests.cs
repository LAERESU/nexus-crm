using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Oracle.ManagedDataAccess.Client;

namespace NexUS.Auth.IntegrationTests;

[Collection(OracleCollection.Name)]
[Trait("Category", "Oracle")]
public class RefreshEndpointTests(OracleAuthFixture fixture)
{
    private const string CookieName = "nexus_rt";

    private HttpClient Client() => fixture.Factory.CreateClient(new() { HandleCookies = false });

    private static string? RefreshCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Where(v => v.StartsWith(CookieName + "=", StringComparison.Ordinal))
                .Select(v => Uri.UnescapeDataString(v.Split(';')[0][(CookieName.Length + 1)..]))
                .FirstOrDefault()
            : null;

    private async Task<string> LoginAsync()
    {
        var response = await Client().PostAsJsonAsync("/api/v1/auth/login",
            new { email = OracleAuthFixture.AdminEmail, password = OracleAuthFixture.AdminPassword });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return RefreshCookie(response)!;
    }

    private Task<HttpResponseMessage> RefreshAsync(string? cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        if (cookie is not null) request.Headers.Add("Cookie", $"{CookieName}={Uri.EscapeDataString(cookie)}");
        return Client().SendAsync(request);
    }

    private async Task<int> CountAsync(string sql, params OracleParameter[] parameters)
    {
        await using var connection = new OracleConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.BindByName = true;
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Refresh_berhasil_memberi_token_baru_dan_token_lama_tidak_bisa_dipakai_lagi()
    {
        var first = await LoginAsync();

        var refreshed = await RefreshAsync(first);

        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
        var second = RefreshCookie(refreshed);
        second.Should().NotBeNullOrEmpty().And.NotBe(first);
        (await refreshed.Content.ReadFromJsonAsync<Dictionary<string, object>>())!.Should().ContainKey("accessToken");

        (await RefreshAsync(first)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Pemakaian_ulang_token_lama_mencabut_semua_sesi_terkait_dan_401()
    {
        var first = await LoginAsync();
        var second = RefreshCookie(await RefreshAsync(first))!;
        var third = RefreshCookie(await RefreshAsync(second))!;

        var reuse = await RefreshAsync(first); // token pertama dicuri dan dipakai lagi

        reuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        RefreshCookie(reuse).Should().BeEmpty("cookie yang ditolak dihapus");
        (await RefreshAsync(third)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "token terbaru di keluarga yang sama ikut dicabut");
    }

    [Fact]
    public async Task Pencabutan_keluarga_tidak_memengaruhi_sesi_lain_user_yang_sama()
    {
        var stolenSession = await LoginAsync();
        var otherSession = await LoginAsync();
        await RefreshAsync(stolenSession);
        await RefreshAsync(stolenSession); // reuse → keluarga sesi pertama dicabut

        (await RefreshAsync(otherSession)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Alasan_pencabutan_tercatat_di_database()
    {
        var first = await LoginAsync();
        await RefreshAsync(first);
        await RefreshAsync(first);

        var reuseDetected = await CountAsync("SELECT COUNT(*) FROM REFRESH_TOKENS WHERE REVOKED_REASON = :reason",
            new OracleParameter("reason", "ReuseDetected"));
        var rotated = await CountAsync("SELECT COUNT(*) FROM REFRESH_TOKENS WHERE REVOKED_REASON = :reason AND REPLACED_BY_ID IS NOT NULL",
            new OracleParameter("reason", "Rotated"));

        reuseDetected.Should().BeGreaterThan(0);
        rotated.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("bukan-token")]
    [InlineData("1.tokenpalsu")]
    public async Task Cookie_kosong_rusak_atau_palsu_401(string? cookie)
    {
        (await RefreshAsync(cookie)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
