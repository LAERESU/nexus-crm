using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NexUS.BuildingBlocks.Auth;

namespace NexUS.BuildingBlocks.Tests.Auth;

/// <summary>Penerbit token tes dengan kunci RSA sendiri dan dokumen JWKS-nya.</summary>
public sealed class TestTokens : IDisposable
{
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RsaSecurityKey _key;

    public TestTokens()
    {
        _key = new RsaSecurityKey(_rsa) { KeyId = Guid.NewGuid().ToString("N") };
        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(_rsa.ExportParameters(false)) { KeyId = _key.KeyId });
        Jwks = JsonSerializer.Serialize(new { keys = new[] { new { kty = jwk.Kty, use = "sig", alg = "RS256", kid = jwk.Kid, n = jwk.N, e = jwk.E } } });
    }

    public string Jwks { get; }

    public string Create(
        string audience = "nexus-api", string issuer = "nexus-auth", long tenantId = 1, string? jti = null, string? sid = "sesi-1",
        DateTimeOffset? expires = null, Guid? userId = null, string[]? roles = null)
    {
        var now = DateTimeOffset.UtcNow;
        var exp = expires ?? now.AddMinutes(15);
        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = (userId ?? Guid.Parse("11111111-1111-1111-1111-111111111111")).ToString(),
            [NexusJwt.TenantClaim] = tenantId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [JwtRegisteredClaimNames.Jti] = jti ?? Guid.NewGuid().ToString("N"),
            ["roles"] = roles ?? ["admin"],
        };
        if (sid is not null) claims[NexusJwt.SessionClaim] = sid;

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = exp.AddMinutes(-15).UtcDateTime,
            NotBefore = exp.AddMinutes(-15).UtcDateTime,
            Expires = exp.UtcDateTime,
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256),
            Claims = claims,
        });
    }

    public void Dispose() => _rsa.Dispose();
}

/// <summary>Blacklist in-memory untuk tes (pengganti Redis).</summary>
public sealed class InMemoryTokenRevocationStore : ITokenRevocationStore
{
    private readonly ConcurrentDictionary<string, bool> _revoked = new();

    public Task RevokeTokenAsync(long tenantId, string jti, DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken ct = default)
    {
        _revoked[RedisTokenRevocationStore.TokenKey(tenantId, jti)] = true;
        return Task.CompletedTask;
    }

    public Task RevokeSessionAsync(long tenantId, string sessionId, CancellationToken ct = default)
    {
        _revoked[RedisTokenRevocationStore.SessionKey(tenantId, sessionId)] = true;
        return Task.CompletedTask;
    }

    public Task<bool> IsRevokedAsync(long tenantId, string jti, string? sessionId, CancellationToken ct = default) =>
        Task.FromResult(_revoked.ContainsKey(RedisTokenRevocationStore.TokenKey(tenantId, jti))
                        || (sessionId is not null && _revoked.ContainsKey(RedisTokenRevocationStore.SessionKey(tenantId, sessionId))));
}
