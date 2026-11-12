using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NexUS.Auth.Application.Abstractions;
using NexUS.Auth.Application.Common;
using NexUS.Auth.Application.Invitations;
using NexUS.Auth.Domain.Credentials;
using NexUS.BuildingBlocks.Common.Exceptions;
using NSubstitute;

namespace NexUS.Auth.UnitTests;

public class CredentialActivationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 8, 0, 0, TimeSpan.Zero);

    private static Credential Invited() => Credential.Invite(1, Guid.NewGuid(), " Budi@Demo.Local ", "hash-token", Now);

    [Fact]
    public void Invite_membuat_kredensial_tanpa_password_dengan_token_72_jam()
    {
        var credential = Invited();

        credential.Email.Should().Be("budi@demo.local");
        credential.PasswordHash.Should().BeNull();
        credential.IsActivated.Should().BeFalse();
        credential.ActivationExpiresAt.Should().Be(Now.AddHours(72));
        credential.CanActivate(Now.AddHours(71)).Should().BeTrue();
    }

    [Fact]
    public void Activate_mengisi_password_menghapus_token_dan_mencatat_event()
    {
        var credential = Invited();

        credential.Activate("hash-password", Now.AddHours(1));

        credential.PasswordHash.Should().Be("hash-password");
        credential.IsActivated.Should().BeTrue();
        credential.ActivationTokenHash.Should().BeNull();
        credential.ActivationExpiresAt.Should().BeNull();
        credential.PopDomainEvents().OfType<CredentialActivatedDomainEvent>().Should().ContainSingle();
    }

    [Fact]
    public void Token_kedaluwarsa_tidak_bisa_dipakai()
    {
        var credential = Invited();

        credential.CanActivate(Now.AddHours(72)).Should().BeFalse();
        FluentActions.Invoking(() => credential.Activate("hash-password", Now.AddHours(72))).Should().Throw<DomainException>();
        credential.PasswordHash.Should().BeNull();
    }

    [Fact]
    public void Token_hanya_bisa_dipakai_sekali_dan_akun_berpassword_tidak_bisa_diaktifkan()
    {
        var credential = Invited();
        credential.Activate("hash-password", Now);

        FluentActions.Invoking(() => credential.Activate("hash-lain", Now)).Should().Throw<DomainException>();
        credential.PasswordHash.Should().Be("hash-password");

        FluentActions.Invoking(() => Credential.Create(1, Guid.NewGuid(), "a@b.c", "hash").Activate("x", Now)).Should().Throw<DomainException>();
    }
}

public class InviteUserCommandHandlerTests
{
    private readonly ICredentialRepository _credentials = Substitute.For<ICredentialRepository>();
    private readonly IProcessedEventStore _processed = Substitute.For<IProcessedEventStore>();
    private readonly IInvitationEmailSender _email = Substitute.For<IInvitationEmailSender>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 12, 8, 0, 0, TimeSpan.Zero));
    private readonly InviteUserCommand _command = new(Guid.NewGuid(), 7, Guid.NewGuid(), "Budi@Demo.Local", "Budi");

    public InviteUserCommandHandlerTests()
    {
        _unitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task>>()(CancellationToken.None));
    }

    private Task Handle() => new InviteUserCommandHandler(
        _credentials, _processed, _email, _unitOfWork, _time, NullLogger<InviteUserCommandHandler>.Instance).Handle(_command, default);

    [Fact]
    public async Task Membuat_kredensial_menandai_event_dan_mengirim_email_dengan_token_yang_hash_nya_tersimpan()
    {
        Credential? stored = null;
        await _credentials.AddAsync(Arg.Do<Credential>(c => stored = c), Arg.Any<CancellationToken>());
        InvitationEmail? sent = null;
        await _email.SendAsync(Arg.Do<InvitationEmail>(e => sent = e), Arg.Any<CancellationToken>());

        await Handle();

        stored!.TenantId.Should().Be(7);
        stored.UserId.Should().Be(_command.UserId);
        stored.Email.Should().Be("budi@demo.local");
        stored.PasswordHash.Should().BeNull();
        sent!.Email.Should().Be("budi@demo.local");
        sent.ExpiresAt.Should().Be(_time.GetUtcNow().AddHours(72));
        sent.ActivationToken.Should().StartWith("7.");
        stored.ActivationTokenHash.Should().Be(ActivationToken.Hash(sent.ActivationToken)).And.NotBe(sent.ActivationToken);
        await _processed.Received(1).MarkAsync(_command.EventId, InviteUserCommandHandler.Consumer, _time.GetUtcNow(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Event_yang_sudah_diproses_tidak_menghasilkan_efek_ganda()
    {
        _processed.IsProcessedAsync(_command.EventId, InviteUserCommandHandler.Consumer, Arg.Any<CancellationToken>()).Returns(true);

        await Handle();

        await _credentials.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _email.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
    }

    [Fact]
    public async Task User_yang_sudah_punya_kredensial_atau_email_terpakai_dilewati_tanpa_email()
    {
        _credentials.FindByUserAsync(7, _command.UserId, Arg.Any<CancellationToken>())
            .Returns(Credential.Create(7, _command.UserId, "budi@demo.local", "hash"));
        await Handle();

        _credentials.FindByUserAsync(7, _command.UserId, Arg.Any<CancellationToken>()).Returns((Credential?)null);
        _credentials.FindByEmailAsync("budi@demo.local", Arg.Any<CancellationToken>())
            .Returns(Credential.Create(9, Guid.NewGuid(), "budi@demo.local", "hash"));
        await Handle();

        await _credentials.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _email.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
        await _processed.Received(2).MarkAsync(_command.EventId, InviteUserCommandHandler.Consumer, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Kegagalan_kirim_email_diteruskan_agar_transaksi_dibatalkan_dan_event_diulang()
    {
        _email.SendAsync(Arg.Any<InvitationEmail>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("SMTP mati")));

        await FluentActions.Awaiting(Handle).Should().ThrowAsync<InvalidOperationException>();
    }
}

public class ActivateAccountCommandHandlerTests
{
    private const string Password = "Rahasia#2026";

    private readonly ICredentialRepository _credentials = Substitute.For<ICredentialRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 12, 8, 0, 0, TimeSpan.Zero));
    private readonly string _token = ActivationToken.Generate(7);
    private readonly Credential _credential;

    public ActivateAccountCommandHandlerTests()
    {
        _credential = Credential.Invite(7, Guid.NewGuid(), "budi@demo.local", ActivationToken.Hash(_token), _time.GetUtcNow());
        _credentials.FindByActivationHashAsync(7, ActivationToken.Hash(_token), Arg.Any<CancellationToken>()).Returns(_credential);
        _hasher.Hash(Password).Returns("hash-password");
    }

    private Task Handle(string token) => new ActivateAccountCommandHandler(
        _credentials, _hasher, _unitOfWork, _time, NullLogger<ActivateAccountCommandHandler>.Instance)
        .Handle(new ActivateAccountCommand(token, Password), default);

    [Fact]
    public async Task Token_valid_mengisi_password_dan_menyimpan()
    {
        await Handle(_token);

        _credential.PasswordHash.Should().Be("hash-password");
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Token_kedaluwarsa_ditolak_tanpa_menghitung_hash_password()
    {
        _time.Advance(TimeSpan.FromHours(72));

        await FluentActions.Awaiting(() => Handle(_token)).Should().ThrowAsync<DomainException>()
            .WithMessage(ActivateAccountCommandHandler.FailureMessage);

        _hasher.DidNotReceiveWithAnyArgs().Hash(default!);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("bukan-token")]
    [InlineData("7.tokenlain")]
    [InlineData("8.tokenlain")]
    public async Task Token_tidak_dikenal_ditolak_dengan_pesan_yang_sama(string token)
    {
        await FluentActions.Awaiting(() => Handle(token)).Should().ThrowAsync<DomainException>()
            .WithMessage(ActivateAccountCommandHandler.FailureMessage);
    }

    [Fact]
    public async Task Pemakaian_bersamaan_hanya_satu_yang_menang()
    {
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns<int>(_ => throw new ConcurrencyException("bentrok"));

        await FluentActions.Awaiting(() => Handle(_token)).Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task Validator_menolak_password_pendek()
    {
        var validator = new ActivateAccountCommandValidator();

        (await validator.ValidateAsync(new ActivateAccountCommand(_token, "pendek"))).IsValid.Should().BeFalse();
        (await validator.ValidateAsync(new ActivateAccountCommand(_token, Password))).IsValid.Should().BeTrue();
    }
}
