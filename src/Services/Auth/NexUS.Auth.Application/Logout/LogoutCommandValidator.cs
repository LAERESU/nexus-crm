using FluentValidation;

namespace NexUS.Auth.Application.Logout;

public sealed class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator()
    {
        RuleFor(x => x.RefreshToken).MaximumLength(256);
        RuleFor(x => x.AccessToken).MaximumLength(8192);
    }
}
