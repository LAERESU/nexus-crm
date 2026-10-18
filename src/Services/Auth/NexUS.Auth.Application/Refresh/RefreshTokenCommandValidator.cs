using FluentValidation;

namespace NexUS.Auth.Application.Refresh;

/// <summary>
/// Batas ukuran input. Cookie yang kosong atau formatnya salah sengaja tidak ditolak di sini (400), melainkan
/// di handler sebagai 401, karena bagi klien artinya sama dengan sesi tidak valid.
/// </summary>
public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(x => x.RefreshToken).MaximumLength(256);
        RuleFor(x => x.IpAddress).MaximumLength(45);
        RuleFor(x => x.UserAgent).MaximumLength(2000);
    }
}
