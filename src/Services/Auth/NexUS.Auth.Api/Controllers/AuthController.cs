using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexUS.Auth.Application.Login;

namespace NexUS.Auth.Api.Controllers;

public sealed record LoginRequest(string Email, string Password);

public sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn);

[ApiController]
[Route("api/v1/auth")]
[AllowAnonymous] // endpoint auth publik (CONTEXT.md bagian 6)
public sealed class AuthController(ISender mediator, TimeProvider timeProvider) : ControllerBase
{
    public const string RefreshTokenCookie = "nexus_rt";

    /// <summary>Login dengan email dan password.</summary>
    /// <remarks>Access token dikembalikan di body (disimpan di memori oleh SPA); refresh token dikirim sebagai cookie httpOnly.</remarks>
    [HttpPost("login")]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<TokenResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(new LoginCommand(
            request.Email,
            request.Password,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString()), ct);

        Response.Cookies.Append(RefreshTokenCookie, result.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api/v1/auth", // hanya dikirim ke endpoint auth (refresh/logout)
            Expires = result.RefreshTokenExpiresAt,
            IsEssential = true,
        });

        var expiresIn = (int)Math.Max(0, (result.AccessTokenExpiresAt - timeProvider.GetUtcNow()).TotalSeconds);
        return Ok(new TokenResponse(result.AccessToken, "Bearer", expiresIn));
    }
}
