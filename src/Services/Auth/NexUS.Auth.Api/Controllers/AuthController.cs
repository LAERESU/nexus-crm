using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexUS.Auth.Application.Login;
using NexUS.Auth.Application.Refresh;
using NexUS.BuildingBlocks.Common.Exceptions;

namespace NexUS.Auth.Api.Controllers;

public sealed record LoginRequest(string Email, string Password);

public sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn);

[ApiController]
[Route("api/v1/auth")]
[AllowAnonymous] // endpoint auth publik (CONTEXT.md bagian 6)
public sealed class AuthController(ISender mediator, TimeProvider timeProvider) : ControllerBase
{
    public const string RefreshTokenCookie = "nexus_rt";
    public const string RefreshTokenCookiePath = "/api/v1/auth"; // hanya dikirim ke endpoint auth

    /// <summary>Login dengan email dan password.</summary>
    /// <remarks>Access token dikembalikan di body (disimpan di memori oleh SPA); refresh token dikirim sebagai cookie httpOnly.</remarks>
    [HttpPost("login")]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<TokenResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(new LoginCommand(request.Email, request.Password, ClientIp(), UserAgent()), ct);
        return Tokens(result);
    }

    /// <summary>Menukar refresh token (cookie) dengan pasangan token baru.</summary>
    /// <remarks>
    /// Refresh token dirotasi setiap dipakai. Pemakaian ulang token lama mencabut seluruh sesi terkait.
    /// Klien wajib melakukan refresh satu per satu (bukan paralel) dengan token terbaru.
    /// </remarks>
    [HttpPost("refresh")]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<TokenResponse>> Refresh(CancellationToken ct)
    {
        try
        {
            var result = await mediator.Send(
                new RefreshTokenCommand(Request.Cookies[RefreshTokenCookie], ClientIp(), UserAgent()), ct);
            return Tokens(result);
        }
        catch (UnauthorizedException)
        {
            // Cookie yang ditolak dihapus agar browser tidak terus mengirimnya.
            Response.Cookies.Delete(RefreshTokenCookie, CookieOptions(expires: null));
            throw;
        }
    }

    private ActionResult<TokenResponse> Tokens(LoginResult result)
    {
        Response.Cookies.Append(RefreshTokenCookie, result.RefreshToken, CookieOptions(result.RefreshTokenExpiresAt));

        var expiresIn = (int)Math.Max(0, (result.AccessTokenExpiresAt - timeProvider.GetUtcNow()).TotalSeconds);
        return Ok(new TokenResponse(result.AccessToken, "Bearer", expiresIn));
    }

    private static CookieOptions CookieOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = RefreshTokenCookiePath,
        Expires = expires,
        IsEssential = true,
    };

    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

    private string UserAgent() => Request.Headers.UserAgent.ToString();
}
