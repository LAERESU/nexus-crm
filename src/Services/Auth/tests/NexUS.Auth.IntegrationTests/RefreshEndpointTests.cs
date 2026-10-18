using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using NexUS.Auth.Application.Abstractions;
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

    /// <summary>Mensimulasikan waktu berlalu melewati masa tenggang rotasi untuk token ini.</summary>
    private async Task AgeRotationAsync(string token)
    {
        await using var connection = new OracleConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.BindByName = true;
        command.CommandText = "UPDATE REFRESH_TOKENS SET REVOKED_AT = REVOKED_AT - INTERVAL '5' MINUTE WHERE TOKEN_HASH = :tokenHash";
        command.Parameters.Add(new OracleParameter("tokenHash", RefreshTokenSecret.Hash(token)));
        (await command.ExecuteNonQueryAsync()).Should().Be(1);
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

        await AgeRotationAsync(first);
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
        await AgeRotationAsync(stolenSession);
        await RefreshAsync(stolenSession); // reuse → keluarga sesi pertama dicabut

        (await RefreshAsync(otherSession)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Alasan_pencabutan_tercatat_di_database_untuk_keluarga_ini()
    {
        var first = await LoginAsync();
        await RefreshAsync(first);
        await AgeRotationAsync(first);
        await RefreshAsync(first);

        const string family = "(SELECT FAMILY_ID FROM REFRESH_TOKENS WHERE TOKEN_HASH = :tokenHash)";
        var hash = RefreshTokenSecret.Hash(first);
        var rotated = await CountAsync(
            $"SELECT COUNT(*) FROM REFRESH_TOKENS WHERE FAMILY_ID = {family} AND REVOKED_REASON = 'Rotated' AND REPLACED_BY_ID IS NOT NULL",
            new OracleParameter("tokenHash", hash));
        var reuseDetected = await CountAsync(
            $"SELECT COUNT(*) FROM REFRESH_TOKENS WHERE FAMILY_ID = {family} AND REVOKED_REASON = 'ReuseDetected'",
            new OracleParameter("tokenHash", hash));
        var active = await CountAsync(
            $"SELECT COUNT(*) FROM REFRESH_TOKENS WHERE FAMILY_ID = {family} AND REVOKED_AT IS NULL",
            new OracleParameter("tokenHash", hash));

        rotated.Should().Be(1);
        reuseDetected.Should().Be(1);
        active.Should().Be(0);
    }

    [Fact]
    public async Task Refresh_bersamaan_dari_tab_lain_ditolak_tanpa_mengeluarkan_sesi()
    {
        var first = await LoginAsync();
        var winner = RefreshCookie(await RefreshAsync(first))!;

        var loser = await RefreshAsync(first); // tab lain memakai token yang sama sesaat kemudian

        loser.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        RefreshCookie(loser).Should().BeNull("cookie baru milik tab pemenang tidak boleh dihapus");
        (await RefreshAsync(winner)).StatusCode.Should().Be(HttpStatusCode.OK, "sesi tetap hidup");
    }

    [Fact]
    public async Task Rotasi_tidak_memperpanjang_umur_sesi()
    {
        var first = await LoginAsync();
        var second = RefreshCookie(await RefreshAsync(first))!;

        var sameExpiry = await CountAsync(
            "SELECT COUNT(*) FROM REFRESH_TOKENS a JOIN REFRESH_TOKENS b ON b.ID = a.REPLACED_BY_ID " +
            "WHERE a.TOKEN_HASH = :tokenHash AND b.EXPIRES_AT = a.EXPIRES_AT",
            new OracleParameter("tokenHash", RefreshTokenSecret.Hash(first)));

        second.Should().NotBeNullOrEmpty();
        sameExpiry.Should().Be(1);
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
