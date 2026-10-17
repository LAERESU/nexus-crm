using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexUS.Auth.Infrastructure.Security;

namespace NexUS.Auth.Api.Controllers;

/// <summary>
/// Kunci publik untuk validasi JWT di Gateway dan service lain. Tidak ada openid-configuration: jwks_uri yang
/// dibangun dari header Host bisa dipalsukan, dan konsumen dikonfigurasi langsung dengan URL JWKS (NX-015).
/// </summary>
[ApiController]
[Route(".well-known")]
[AllowAnonymous]
public sealed class WellKnownController(JwtSigningKey signingKey) : ControllerBase
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
}
