using MediatR;

namespace NexUS.Auth.Application.Login;

public sealed record LoginCommand(string Email, string Password, string? IpAddress, string? UserAgent) : IRequest<LoginResult>;

public sealed record LoginResult(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);
