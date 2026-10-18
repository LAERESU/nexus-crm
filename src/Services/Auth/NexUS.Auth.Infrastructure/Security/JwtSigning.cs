using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NexUS.Auth.Application.Abstractions;
using NexUS.Auth.Domain.Credentials;
using NexUS.BuildingBlocks.Auth;

namespace NexUS.Auth.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Auth:Jwt";

    public string Issuer { get; set; } = "nexus-auth";
    public string Audience { get; set; } = "nexus-api";
    public TimeSpan AccessTokenLifetime { get; set; } = NexusJwt.AccessTokenLifetime;

    /// <summary>Kunci privat RSA (PEM) dari secret. Wajib di luar Development.</summary>
    public string? SigningKeyPem { get; set; }
}

/// <summary>Kunci tanda tangan RS256 dan kunci publiknya dalam bentuk JWK untuk endpoint JWKS.</summary>
public sealed class JwtSigningKey : IDisposable
{
    private readonly RSA _rsa;

    public JwtSigningKey(IOptions<JwtOptions> options, IHostEnvironment environment, ILogger<JwtSigningKey> logger)
    {
        _rsa = RSA.Create();
        var pem = options.Value.SigningKeyPem;
        if (!string.IsNullOrWhiteSpace(pem))
        {
            _rsa.ImportFromPem(pem);
        }
        else if (environment.IsDevelopment())
        {
            _rsa.KeySize = 2048;
            logger.LogWarning("Auth:Jwt:SigningKeyPem tidak diisi; memakai kunci RSA sementara (hanya Development, token tidak berlaku setelah restart)");
        }
        else
        {
            throw new InvalidOperationException("Auth:Jwt:SigningKeyPem wajib diisi dari secret di luar Development.");
        }

        var publicKey = new RsaSecurityKey(_rsa.ExportParameters(includePrivateParameters: false));
        var keyId = Base64UrlEncoder.Encode(JsonWebKeyConverter.ConvertFromRSASecurityKey(publicKey).ComputeJwkThumbprint());

        Credentials = new SigningCredentials(new RsaSecurityKey(_rsa) { KeyId = keyId }, SecurityAlgorithms.RsaSha256);

        PublicJwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(publicKey);
        PublicJwk.Kid = keyId;
        PublicJwk.Use = JsonWebKeyUseNames.Sig;
        PublicJwk.Alg = SecurityAlgorithms.RsaSha256;
    }

    public SigningCredentials Credentials { get; }
    public JsonWebKey PublicJwk { get; }

    public void Dispose() => _rsa.Dispose();
}

public sealed class JwtAccessTokenIssuer(JwtSigningKey signingKey, IOptions<JwtOptions> options, TimeProvider timeProvider)
    : IAccessTokenIssuer
{
    private static readonly JsonWebTokenHandler Handler = new();

    public AccessToken Issue(Credential credential, IReadOnlyCollection<string> roles, Guid sessionId)
    {
        var now = timeProvider.GetUtcNow();
        var expiresAt = now + options.Value.AccessTokenLifetime;

        var token = Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Value.Issuer,
            Audience = options.Value.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = signingKey.Credentials,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = credential.UserId.ToString(),
                [NexusJwt.TenantClaim] = credential.TenantId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                [NexusJwt.SessionClaim] = sessionId.ToString("N"),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
                ["roles"] = roles.ToArray(),
            },
        });

        return new AccessToken(token, expiresAt);
    }
}
