using System.Globalization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NexUS.Auth.Application.Abstractions;
using NexUS.BuildingBlocks.Auth;

namespace NexUS.Auth.Infrastructure.Security;

/// <summary>Membaca access token yang diterbitkan Auth sendiri; masa berlaku sengaja tidak diperiksa.</summary>
public sealed class JwtAccessTokenReader(JwtSigningKey signingKey, IOptions<JwtOptions> options) : IAccessTokenReader
{
    private static readonly JsonWebTokenHandler Handler = new();

    public async Task<AccessTokenInfo?> ReadAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var result = await Handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = options.Value.Issuer,
            ValidAudience = options.Value.Audience,
            IssuerSigningKey = signingKey.Credentials.Key,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ValidateLifetime = false, // token kedaluwarsa tetap dibaca, lalu diabaikan oleh blacklist
        });
        if (!result.IsValid || result.SecurityToken is not JsonWebToken jwt) return null;

        return long.TryParse(jwt.GetClaim("tid")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var tenantId)
               && !string.IsNullOrWhiteSpace(jwt.Id)
            ? new AccessTokenInfo(tenantId, jwt.Id, jwt.ValidTo == DateTime.MinValue ? DateTimeOffset.MinValue : new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero))
            : null;
    }
}

public sealed class AccessTokenBlacklist(ITokenRevocationStore store) : IAccessTokenBlacklist
{
    public Task RevokeAsync(AccessTokenInfo token, DateTimeOffset now, CancellationToken ct) =>
        store.RevokeAsync(token.TenantId, token.Jti, token.ExpiresAt, now, ct);
}
