using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NexUS.User.IntegrationTests;

/// <summary>
/// Pengganti validasi JWT di tes: identitas dibaca dari header <c>X-Test-*</c>, sehingga setiap request bisa
/// berperan sebagai tenant/user yang berbeda tanpa menjalankan Auth Service.
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string TenantHeader = "X-Test-Tenant";
    public const string UserHeader = "X-Test-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(TenantHeader, out var tenant) || !Request.Headers.TryGetValue(UserHeader, out var user))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim> { new("sub", user.ToString()), new("tid", tenant.ToString()) };
        var identity = new ClaimsIdentity(claims, SchemeName, nameType: "sub", roleType: ClaimTypes.Role);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
