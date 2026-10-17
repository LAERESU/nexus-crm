using FluentAssertions;
using FluentValidation;
using MediatR;
using NexUS.Tasks.Application.Common.Behaviors;

namespace NexUS.Tasks.UnitTests;

public class ValidationBehaviorTests
{
    private sealed record Ping(string Name) : IRequest<string>;

    private sealed class PingValidator : AbstractValidator<Ping>
    {
        public PingValidator() => RuleFor(x => x.Name).NotEmpty().WithMessage("Nama wajib diisi.");
    }

    [Fact]
    public async Task Request_tidak_valid_melempar_ValidationException_dan_handler_tidak_dipanggil()
    {
        var sut = new ValidationBehavior<Ping, string>([new PingValidator()]);
        var dipanggil = false;

        var act = () => sut.Handle(new Ping(""), _ =>
        {
            dipanggil = true;
            return Task.FromResult("ok");
        }, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainSingle(e => e.ErrorMessage == "Nama wajib diisi.");
        dipanggil.Should().BeFalse();
    }

    [Fact]
    public async Task Request_valid_diteruskan_ke_handler()
    {
        var sut = new ValidationBehavior<Ping, string>([new PingValidator()]);

        var result = await sut.Handle(new Ping("a"), _ => Task.FromResult("ok"), CancellationToken.None);

        result.Should().Be("ok");
    }

    [Fact]
    public async Task Tanpa_validator_request_langsung_diteruskan()
    {
        var sut = new ValidationBehavior<Ping, string>([]);

        var result = await sut.Handle(new Ping(""), _ => Task.FromResult("ok"), CancellationToken.None);

        result.Should().Be("ok");
    }
}
