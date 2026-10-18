using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NexUS.Auth.Application.Abstractions;
using NexUS.Auth.Application.Common;
using NexUS.Auth.Application.Logout;
using NexUS.Auth.Domain.Credentials;
using NexUS.Auth.Infrastructure.Security;
using NSubstitute;

namespace NexUS.Auth.UnitTests;

public class LogoutCommandHandlerTests
{
    private readonly IRefreshTokenRepository _tokens = Substitute.For<IRefreshTokenRepository>();
    private readonly ICredentialRepository _credentials = Substitute.For<ICredentialRepository>();
    private readonly IAccessTokenReader _reader = Substitute.For<IAccessTokenReader>();
    private readonly IAccessTokenBlacklist _blacklist = Substitute.For<IAccessTokenBlacklist>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 18, 8, 0, 0, TimeSpan.Zero));
    private readonly Credential _credential = Credential.Create(3, Guid.NewGuid(), "budi@demo.local", "hash");

    private Task Logout(string? refresh, string? access) =>
        new LogoutCommandHandler(_tokens, _credentials, _reader, _blacklist, _unitOfWork, _time,
                NullLogger<LogoutCommandHandler>.Instance)
            .Handle(new LogoutCommand(refresh, access), CancellationToken.None);

    [Fact]
    public async Task Logout_mencabut_keluarga_refresh_token_dan_memasukkan_access_token_ke_blacklist()
    {
        var refreshValue = RefreshTokenSecret.Generate(3);
        var refresh = RefreshToken.Issue(_credential, RefreshTokenSecret.Hash(refreshValue), _time.GetUtcNow(), null, null);
        var access = new AccessTokenInfo(3, "jti-1", _time.GetUtcNow().AddMinutes(10));
        _tokens.FindByHashAsync(3, RefreshTokenSecret.Hash(refreshValue), Arg.Any<CancellationToken>()).Returns(refresh);
        _credentials.FindByIdAsync(3, Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(_credential);
        _reader.ReadAsync("akses").Returns(access);

        await Logout(refreshValue, "akses");

        await _blacklist.Received(1).RevokeAsync(access, _time.GetUtcNow(), Arg.Any<CancellationToken>());
        await _tokens.Received(1).RevokeFamilyAsync(3, refresh.FamilyId, _time.GetUtcNow(),
            RefreshTokenRevocationReason.Logout, Arg.Any<CancellationToken>());
        _credential.DomainEvents.Should().ContainSingle(e => e is UserLoggedOutDomainEvent);
    }

    [Fact]
    public async Task Tanpa_token_apa_pun_logout_tetap_berhasil_tanpa_efek()
    {
        await Logout(null, null);

        await _blacklist.DidNotReceiveWithAnyArgs().RevokeAsync(default!, default, default);
        await _tokens.DidNotReceiveWithAnyArgs().FindByHashAsync(default, default!, default);
    }

    [Fact]
    public async Task Access_token_tidak_valid_tidak_dimasukkan_ke_blacklist()
    {
        _reader.ReadAsync("palsu").Returns((AccessTokenInfo?)null);

        await Logout(null, "palsu");

        await _blacklist.DidNotReceiveWithAnyArgs().RevokeAsync(default!, default, default);
    }

    [Fact]
    public async Task Refresh_token_tidak_dikenal_diabaikan()
    {
        await Logout(RefreshTokenSecret.Generate(3), null);

        await _tokens.DidNotReceiveWithAnyArgs().RevokeFamilyAsync(default, default, default, default, default);
    }
}

public class JwtAccessTokenReaderTests
{
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly JwtOptions _options = new();

    private JwtSigningKey NewKey()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Development);
        return new JwtSigningKey(Options.Create(_options), environment, NullLogger<JwtSigningKey>.Instance);
    }

    [Fact]
    public async Task Token_sendiri_dibaca_termasuk_yang_sudah_kedaluwarsa()
    {
        var key = NewKey();
        var credential = Credential.Create(9, Guid.NewGuid(), "a@b.c", "hash");
        var token = new JwtAccessTokenIssuer(key, Options.Create(_options), _time).Issue(credential, []);
        _time.Advance(TimeSpan.FromHours(1));

        var info = await new JwtAccessTokenReader(key, Options.Create(_options)).ReadAsync(token.Value);

        info.Should().NotBeNull();
        info!.TenantId.Should().Be(9);
        info.Jti.Should().NotBeNullOrEmpty();
        info.ExpiresAt.Should().BeCloseTo(token.ExpiresAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Token_yang_ditandatangani_kunci_lain_ditolak()
    {
        var credential = Credential.Create(9, Guid.NewGuid(), "a@b.c", "hash");
        var foreign = new JwtAccessTokenIssuer(NewKey(), Options.Create(_options), _time).Issue(credential, []);

        (await new JwtAccessTokenReader(NewKey(), Options.Create(_options)).ReadAsync(foreign.Value)).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bukan.jwt.sama-sekali")]
    public async Task Token_kosong_atau_rusak_menghasilkan_null(string? token)
    {
        (await new JwtAccessTokenReader(NewKey(), Options.Create(_options)).ReadAsync(token)).Should().BeNull();
    }
}
