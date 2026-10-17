using FluentValidation;
using MediatR;

namespace NexUS.Auth.Application.Login;

public sealed record LoginCommand(string Email, string Password, string? IpAddress, string? UserAgent) : IRequest<LoginResult>;

public sealed record LoginResult(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(320).EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MaximumLength(200);
    }
}
