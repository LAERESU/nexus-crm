using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NexUS.Auth.Domain.Credentials;
using NexUS.Auth.Infrastructure.Security;
using NSubstitute;

namespace NexUS.Auth.UnitTests;

public class JwtAccessTokenIssuerTests
{
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly JwtOptions _options = new();
    private readonly JwtSigningKey _key;
    private readonly Credential _credential = Credential.Create(42, Guid.NewGuid(), "budi@demo.local", "hash");

    public JwtAccessTokenIssuerTests()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Development);
        _key = new JwtSigningKey(Options.Create(_options), environment, NullLogger<JwtSigningKey>.Instance);
    }

    private JwtAccessTokenIssuer Issuer() => new(_key, Options.Create(_options), _time);

    /// <summary>Memvalidasi token hanya dengan kunci publik dari JWKS, seperti yang dilakukan service lain.</summary>
    private async Task<TokenValidationResult> ValidateWithJwks(string token)
    {
        var jwksJson = JsonSerializer.Serialize(new { keys = new[] { _key.PublicJwk } });
        var keys = new JsonWebKeySet(jwksJson).GetSigningKeys();

        return await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = _options.Issuer,
            ValidAudience = _options.Audience,
            IssuerSigningKeys = keys,
            ClockSkew = TimeSpan.Zero,
        });
    }

    [Fact]
    public async Task Token_bisa_diverifikasi_dengan_JWKS_dan_memuat_klaim_wajib()
    {
        var token = Issuer().Issue(_credential, ["admin", "pm"]);

        var result = await ValidateWithJwks(token.Value);

        result.IsValid.Should().BeTrue(result.Exception?.Message);
        var jwt = (JsonWebToken)result.SecurityToken;
        jwt.Alg.Should().Be("RS256");
        jwt.Kid.Should().Be(_key.PublicJwk.Kid);
        jwt.Subject.Should().Be(_credential.UserId.ToString());
        jwt.GetClaim("tid").Value.Should().Be("42");
        jwt.Id.Should().NotBeNullOrEmpty();
        jwt.Claims.Where(c => c.Type == "roles").Select(c => c.Value).Should().BeEquivalentTo("admin", "pm");
    }

    [Fact]
    public void Token_berlaku_15_menit()
    {
        var token = Issuer().Issue(_credential, []);

        token.ExpiresAt.Should().Be(_time.GetUtcNow().AddMinutes(15));
    }

    [Fact]
    public async Task Token_yang_diubah_gagal_diverifikasi()
    {
        var token = Issuer().Issue(_credential, ["admin"]).Value;
        var parts = token.Split('.');
        var tampered = $"{parts[0]}.{Base64UrlEncoder.Encode("{\"sub\":\"lain\",\"tid\":\"1\"}")}.{parts[2]}";

        (await ValidateWithJwks(tampered)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void JWKS_hanya_berisi_kunci_publik()
    {
        _key.PublicJwk.D.Should().BeNullOrEmpty();
        _key.PublicJwk.P.Should().BeNullOrEmpty();
        _key.PublicJwk.Kty.Should().Be("RSA");
    }

    [Fact]
    public void Di_luar_Development_tanpa_kunci_aplikasi_gagal_start()
    {
        var production = Substitute.For<IHostEnvironment>();
        production.EnvironmentName.Returns(Environments.Production);

        var act = () => new JwtSigningKey(Options.Create(new JwtOptions()), production, NullLogger<JwtSigningKey>.Instance);

        act.Should().Throw<InvalidOperationException>();
    }
}
