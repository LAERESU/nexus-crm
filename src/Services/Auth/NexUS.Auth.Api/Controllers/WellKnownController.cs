using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NexUS.Auth.Infrastructure.Security;

namespace NexUS.Auth.Api.Controllers;

/// <summary>Discovery dan kunci publik untuk validasi JWT di Gateway dan service lain.</summary>
[ApiController]
[Route(".well-known")]
[AllowAnonymous]
public sealed class WellKnownController(JwtSigningKey signingKey, IOptions<JwtOptions> options) : ControllerBase
{
    [HttpGet("jwks.json")]
    [ResponseCache(Duration = 300)]
    public IActionResult Jwks() => Ok(new
    {
        keys = new[]
        {
            new
            {
                kty = signingKey.PublicJwk.Kty,
                use = signingKey.PublicJwk.Use,
                alg = signingKey.PublicJwk.Alg,
                kid = signingKey.PublicJwk.Kid,
                n = signingKey.PublicJwk.N,
                e = signingKey.PublicJwk.E,
            },
        },
    });

    [HttpGet("openid-configuration")]
    [ResponseCache(Duration = 300)]
    public IActionResult OpenIdConfiguration()
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        return Ok(new
        {
            issuer = options.Value.Issuer,
            jwks_uri = $"{baseUrl}/.well-known/jwks.json",
            id_token_signing_alg_values_supported = new[] { "RS256" },
        });
    }
}
