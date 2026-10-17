using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NexUS.Auth.Application.Abstractions;
using NexUS.Auth.Application.Common;
using NexUS.Auth.Application.Login;
using NexUS.Auth.Domain.Credentials;
using NexUS.BuildingBlocks.Common.Exceptions;
using NSubstitute;

namespace NexUS.Auth.UnitTests;

public class LoginCommandHandlerTests
{
    private const string Password = "Rahasia#2026";

    private readonly ICredentialRepository _credentials = Substitute.For<ICredentialRepository>();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly ILoginAttemptRepository _attempts = Substitute.For<ILoginAttemptRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IAccessTokenIssuer _issuer = Substitute.For<IAccessTokenIssuer>();
    private readonly IUserDirectory _directory = Substitute.For<IUserDirectory>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 18, 8, 0, 0, TimeSpan.Zero));
    private readonly Credential _credential = Credential.Create(7, Guid.NewGuid(), "budi@demo.local", "hash-asli");

    public LoginCommandHandlerTests()
    {
        _hasher.Verify(Password, "hash-asli").Returns(true);
        _directory.GetRolesAsync(Arg.Any<Credential>(), Arg.Any<CancellationToken>()).Returns(["admin"]);
        _issuer.Issue(Arg.Any<Credential>(), Arg.Any<IReadOnlyCollection<string>>())
            .Returns(new AccessToken("jwt", _time.GetUtcNow().AddMinutes(15)));
    }

    private LoginCommandHandler Handler() => new(
        _credentials, _refreshTokens, _attempts, _hasher, _issuer, _directory, _unitOfWork, _time,
        NullLogger<LoginCommandHandler>.Instance);

    private Task<LoginResult> Login(string email = "Budi@Demo.Local", string password = Password) =>
        Handler().Handle(new LoginCommand(email, password, "10.0.0.1", "browser"), CancellationToken.None);

    [Fact]
    public async Task Login_benar_menerbitkan_token_dan_menyimpan_hash_refresh_token()
    {
        _credentials.FindByEmailAsync("budi@demo.local", Arg.Any<CancellationToken>()).Returns(_credential);
        RefreshToken? stored = null;
        await _refreshTokens.AddAsync(Arg.Do<RefreshToken>(t => stored = t), Arg.Any<CancellationToken>());

        var result = await Login();

        result.AccessToken.Should().Be("jwt");
        result.RefreshToken.Should().NotBeNullOrEmpty();
        stored!.TokenHash.Should().Be(RefreshTokenSecret.Hash(result.RefreshToken)).And.NotBe(result.RefreshToken);
        _issuer.Received(1).Issue(_credential, Arg.Is<IReadOnlyCollection<string>>(r => r.Contains("admin")));
        await _attempts.Received(1).AddAsync(Arg.Is<LoginAttempt>(a => a.Succeeded), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Email_tidak_dikenal_401_generik_dan_tetap_menjalankan_verifikasi_palsu()
    {
        var act = () => Login("tidak@ada.local");

        (await act.Should().ThrowAsync<UnauthorizedException>()).Which.Message.Should().Be(LoginCommandHandler.GenericFailureMessage);
        _hasher.Received(1).VerifyDummy(Password);
        await _attempts.Received(1).AddAsync(
            Arg.Is<LoginAttempt>(a => !a.Succeeded && a.FailureReason == LoginFailureReason.UnknownEmail), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Password_salah_401_generik_dan_menambah_hitungan_gagal()
    {
        _credentials.FindByEmailAsync("budi@demo.local", Arg.Any<CancellationToken>()).Returns(_credential);

        var act = () => Login(password: "salah");

        (await act.Should().ThrowAsync<UnauthorizedException>()).Which.Message.Should().Be(LoginCommandHandler.GenericFailureMessage);
        _credential.FailedAttempts.Should().Be(1);
        _issuer.DidNotReceiveWithAnyArgs().Issue(default!, default!);
    }

    [Fact]
    public async Task Gagal_enam_kali_berturut_turut_akun_terkunci_walau_password_benar()
    {
        _credentials.FindByEmailAsync("budi@demo.local", Arg.Any<CancellationToken>()).Returns(_credential);
        for (var i = 0; i < Credential.MaxFailedAttempts; i++)
            await FluentActions.Awaiting(() => Login(password: "salah")).Should().ThrowAsync<UnauthorizedException>();

        var act = () => Login(); // percobaan ke-6 dengan password benar

        (await act.Should().ThrowAsync<UnauthorizedException>()).Which.Message.Should().Be(LoginCommandHandler.GenericFailureMessage);
        await _attempts.Received(1).AddAsync(
            Arg.Is<LoginAttempt>(a => a.FailureReason == LoginFailureReason.Locked), Arg.Any<CancellationToken>());
        _issuer.DidNotReceiveWithAnyArgs().Issue(default!, default!);
    }

    [Fact]
    public async Task Setelah_masa_kunci_habis_login_benar_berhasil()
    {
        _credentials.FindByEmailAsync("budi@demo.local", Arg.Any<CancellationToken>()).Returns(_credential);
        for (var i = 0; i < Credential.MaxFailedAttempts; i++)
            await FluentActions.Awaiting(() => Login(password: "salah")).Should().ThrowAsync<UnauthorizedException>();

        _time.Advance(Credential.LockDuration);

        (await Login()).AccessToken.Should().Be("jwt");
    }

    [Fact]
    public async Task Akun_terkunci_tetap_menjalankan_verifikasi_password_agar_waktu_respons_sama()
    {
        _credentials.FindByEmailAsync("budi@demo.local", Arg.Any<CancellationToken>()).Returns(_credential);
        for (var i = 0; i < Credential.MaxFailedAttempts; i++)
            await FluentActions.Awaiting(() => Login(password: "salah")).Should().ThrowAsync<UnauthorizedException>();
        _hasher.ClearReceivedCalls();

        await FluentActions.Awaiting(() => Login()).Should().ThrowAsync<UnauthorizedException>();

        _hasher.Received(1).Verify(Password, "hash-asli");
    }

    [Fact]
    public async Task Konflik_konkurensi_saat_mencatat_gagal_dimuat_ulang_lalu_disimpan_lagi()
    {
        _credentials.FindByEmailAsync("budi@demo.local", Arg.Any<CancellationToken>()).Returns(_credential);
        var calls = 0;
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            ++calls == 1 ? throw new ConcurrencyException("bentrok") : Task.FromResult(1));

        await FluentActions.Awaiting(() => Login(password: "salah")).Should().ThrowAsync<UnauthorizedException>();

        await _credentials.Received(1).ReloadAsync(_credential, Arg.Any<CancellationToken>());
        calls.Should().Be(2);
    }

    [Fact]
    public async Task Konflik_konkurensi_terus_menerus_berhenti_setelah_batas_percobaan()
    {
        _credentials.FindByEmailAsync("budi@demo.local", Arg.Any<CancellationToken>()).Returns(_credential);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns<int>(_ => throw new ConcurrencyException("bentrok"));

        await FluentActions.Awaiting(() => Login(password: "salah")).Should().ThrowAsync<ConcurrencyException>();

        await _credentials.Received(LoginCommandHandler.MaxConcurrencyRetries - 1).ReloadAsync(_credential, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("", Password)]
    [InlineData("bukan-email", Password)]
    [InlineData("budi@demo.local", "")]
    public void Validator_menolak_input_tidak_valid(string email, string password)
    {
        new LoginCommandValidator().Validate(new LoginCommand(email, password, null, null)).IsValid.Should().BeFalse();
    }
}
