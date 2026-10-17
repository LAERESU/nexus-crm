using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NexUS.Auth.Application.Abstractions;
using NexUS.Auth.Domain.Credentials;
using NexUS.Auth.Infrastructure.Persistence;
using Oracle.ManagedDataAccess.Client;

namespace NexUS.Auth.IntegrationTests;

[Collection(OracleCollection.Name)]
[Trait("Category", "Oracle")]
public class LoginEndpointTests(OracleAuthFixture fixture)
{
    private sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn);

    private HttpClient Client() => fixture.Factory.CreateClient(new() { HandleCookies = false });

    private Task<HttpResponseMessage> Login(string email, string password) =>
        Client().PostAsJsonAsync("/api/v1/auth/login", new { email, password });

    private async Task<string> CreateUserAsync(string email, string password)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        db.Credentials.Add(Credential.Create(1, Guid.NewGuid(), email, hasher.Hash(password)));
        await db.SaveChangesAsync();
        return email;
    }

    private async Task<object?> ScalarAsync(string sql, params OracleParameter[] parameters)
    {
        await using var connection = new OracleConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.BindByName = true;
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        return await command.ExecuteScalarAsync();
    }

    [Fact]
    public async Task Login_benar_200_dengan_access_token_dan_cookie_refresh_token()
    {
        var response = await Login(OracleAuthFixture.AdminEmail, OracleAuthFixture.AdminPassword);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>();
        body!.AccessToken.Should().NotBeNullOrEmpty();
        body.TokenType.Should().Be("Bearer");
        body.ExpiresIn.Should().BeInRange(890, 900);

        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("nexus_rt=", StringComparison.Ordinal));
        cookie.ToLowerInvariant().Should().Contain("httponly").And.Contain("secure").And.Contain("samesite=strict").And.Contain("path=/api/v1/auth");
    }

    [Fact]
    public async Task Refresh_token_disimpan_sebagai_hash_dan_event_login_masuk_outbox()
    {
        var response = await Login(OracleAuthFixture.AdminEmail, OracleAuthFixture.AdminPassword);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("nexus_rt=", StringComparison.Ordinal));
        var rawToken = Uri.UnescapeDataString(cookie.Split(';')[0]["nexus_rt=".Length..]);

        var byHash = await ScalarAsync("SELECT COUNT(*) FROM REFRESH_TOKENS WHERE TOKEN_HASH = :tokenHash",
            new OracleParameter("tokenHash", RefreshTokenSecret.Hash(rawToken)));
        // Nama bind "raw" tidak boleh dipakai: RAW adalah kata tercadang Oracle (ORA-01745).
        var byRaw = await ScalarAsync("SELECT COUNT(*) FROM REFRESH_TOKENS WHERE TOKEN_HASH = :plainToken",
            new OracleParameter("plainToken", rawToken));
        var outbox = await ScalarAsync("SELECT COUNT(*) FROM OUTBOX_MESSAGES WHERE TYPE LIKE '%UserLoggedInDomainEvent%'");

        Convert.ToInt32(byHash).Should().Be(1);
        Convert.ToInt32(byRaw).Should().Be(0);
        Convert.ToInt32(outbox).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Login_salah_dan_email_tidak_dikenal_401_dengan_pesan_yang_sama()
    {
        var wrongPassword = await Login(OracleAuthFixture.AdminEmail, "salah-sekali");
        var unknownEmail = await Login("tidak.ada@demo.local", "salah-sekali");

        wrongPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        unknownEmail.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var a = JsonDocument.Parse(await wrongPassword.Content.ReadAsStringAsync()).RootElement;
        var b = JsonDocument.Parse(await unknownEmail.Content.ReadAsStringAsync()).RootElement;
        a.GetProperty("detail").GetString().Should().Be(b.GetProperty("detail").GetString());
        a.GetProperty("title").GetString().Should().Be(b.GetProperty("title").GetString());
        wrongPassword.Headers.Contains("Set-Cookie").Should().BeFalse();
    }

    [Fact]
    public async Task Enam_kali_gagal_berturut_turut_akun_terkunci_walau_password_benar()
    {
        var email = await CreateUserAsync("kunci.test@demo.local", "Benar#2026");

        for (var i = 0; i < Credential.MaxFailedAttempts; i++)
            (await Login(email, "salah")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var sixth = await Login(email, "Benar#2026");

        sixth.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var locked = await ScalarAsync(
            "SELECT COUNT(*) FROM CREDENTIALS WHERE EMAIL = :email AND LOCKED_UNTIL > SYSTIMESTAMP",
            new OracleParameter("email", email));
        Convert.ToInt32(locked).Should().Be(1);
    }

    [Fact]
    public async Task Access_token_bisa_diverifikasi_memakai_JWKS()
    {
        var login = await Login(OracleAuthFixture.AdminEmail, OracleAuthFixture.AdminPassword);
        var token = (await login.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken;

        var jwks = await Client().GetStringAsync("/.well-known/jwks.json");
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = "nexus-auth",
            ValidAudience = "nexus-api",
            IssuerSigningKeys = new JsonWebKeySet(jwks).GetSigningKeys(),
        });

        result.IsValid.Should().BeTrue(result.Exception?.Message);
        var jwt = (JsonWebToken)result.SecurityToken;
        jwt.GetClaim("tid").Value.Should().Be("1");
        jwt.Claims.Where(c => c.Type == "roles").Select(c => c.Value).Should().Contain("admin");
    }
}
