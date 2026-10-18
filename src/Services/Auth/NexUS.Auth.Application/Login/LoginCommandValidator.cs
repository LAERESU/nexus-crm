using FluentValidation;

namespace NexUS.Auth.Application.Login;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(320).EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MaximumLength(200);
    }
}
