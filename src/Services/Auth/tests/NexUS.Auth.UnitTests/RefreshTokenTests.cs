using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NexUS.Auth.Application.Abstractions;
using NexUS.Auth.Application.Common;
using NexUS.Auth.Application.Refresh;
using NexUS.Auth.Domain.Credentials;
using NexUS.BuildingBlocks.Common.Exceptions;
using NSubstitute;

namespace NexUS.Auth.UnitTests;

public class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 8, 0, 0, TimeSpan.Zero);
    private static readonly Credential Credential = Credential.Create(5, Guid.NewGuid(), "budi@demo.local", "hash");

    [Fact]
    public void Rotasi_mencabut_token_lama_dan_menerbitkan_token_baru_satu_keluarga()
    {
        var current = RefreshToken.Issue(Credential, "lama", Now, "ua", "ip");

        var next = current.Rotate("baru", Now.AddHours(1), "ua", "ip");

        current.IsRevoked.Should().BeTrue();
        current.RevokedReason.Should().Be(RefreshTokenRevocationReason.Rotated);
        current.ReplacedBy.Should().BeSameAs(next);
        next.FamilyId.Should().Be(current.FamilyId);
        next.TenantId.Should().Be(current.TenantId);
        next.IsActive(Now.AddHours(1)).Should().BeTrue();
        next.ExpiresAt.Should().Be(current.ExpiresAt, "rotasi tidak memperpanjang umur sesi");
    }

    [Fact]
    public void Token_baru_dirotasi_dikenali_selama_masa_tenggang()
    {
        var current = RefreshToken.Issue(Credential, "lama", Now, null, null);
        current.Rotate("baru", Now, null, null);

        current.WasJustRotated(Now + RefreshToken.ReuseGracePeriod).Should().BeTrue();
        current.WasJustRotated(Now + RefreshToken.ReuseGracePeriod + TimeSpan.FromSeconds(1)).Should().BeFalse();
    }

    [Fact]
    public void Token_yang_dicabut_karena_alasan_lain_bukan_rotasi_baru()
    {
        var token = RefreshToken.Issue(Credential, "x", Now, null, null);
        token.Revoke(Now, RefreshTokenRevocationReason.Logout);

        token.WasJustRotated(Now).Should().BeFalse();
    }

    [Fact]
    public void Login_baru_memulai_keluarga_baru()
    {
        RefreshToken.Issue(Credential, "a", Now, null, null).FamilyId
            .Should().NotBe(RefreshToken.Issue(Credential, "b", Now, null, null).FamilyId);
    }

    [Fact]
    public void Token_yang_sudah_dicabut_tidak_bisa_dirotasi()
    {
        var current = RefreshToken.Issue(Credential, "lama", Now, null, null);
        current.Rotate("baru", Now, null, null);

        var act = () => current.Rotate("lagi", Now, null, null);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Revoke_idempoten_dan_mempertahankan_alasan_pertama()
    {
        var token = RefreshToken.Issue(Credential, "x", Now, null, null);

        token.Revoke(Now, RefreshTokenRevocationReason.ReuseDetected);
        token.Revoke(Now.AddMinutes(5), RefreshTokenRevocationReason.Logout);

        token.RevokedAt.Should().Be(Now);
        token.RevokedReason.Should().Be(RefreshTokenRevocationReason.ReuseDetected);
    }

    [Theory]
    [InlineData("12.abcDEF_-", true, 12)]
    [InlineData("0.abc", false, 0)]
    [InlineData("-1.abc", false, 0)]
    [InlineData("abc", false, 0)]
    [InlineData(".abc", false, 0)]
    [InlineData("12.", false, 0)]
    [InlineData("", false, 0)]
    [InlineData(null, false, 0)]
    public void Tenant_dibaca_dari_awalan_token(string? token, bool valid, long tenantId)
    {
        RefreshTokenSecret.TryGetTenantId(token, out var parsed).Should().Be(valid);
        if (valid) parsed.Should().Be(tenantId);
    }

    [Fact]
    public void Token_yang_dihasilkan_diawali_tenant()
    {
        var token = RefreshTokenSecret.Generate(77);

        RefreshTokenSecret.TryGetTenantId(token, out var tenantId).Should().BeTrue();
        tenantId.Should().Be(77);
        token.Length.Should().BeGreaterThan(40);
    }
}

public class RefreshTokenCommandHandlerTests
{
    private readonly IRefreshTokenRepository _tokens = Substitute.For<IRefreshTokenRepository>();
    private readonly ICredentialRepository _credentials = Substitute.For<ICredentialRepository>();
    private readonly IAccessTokenIssuer _issuer = Substitute.For<IAccessTokenIssuer>();
    private readonly IUserDirectory _directory = Substitute.For<IUserDirectory>();
    private readonly IAccessTokenBlacklist _blacklist = Substitute.For<IAccessTokenBlacklist>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 18, 8, 0, 0, TimeSpan.Zero));
    private readonly Credential _credential = Credential.Create(5, Guid.NewGuid(), "budi@demo.local", "hash");
    private readonly string _tokenValue = RefreshTokenSecret.Generate(5);
    private readonly RefreshToken _current;

    public RefreshTokenCommandHandlerTests()
    {
        _current = RefreshToken.Issue(_credential, RefreshTokenSecret.Hash(_tokenValue), _time.GetUtcNow(), null, null);
        _tokens.FindByHashAsync(5, RefreshTokenSecret.Hash(_tokenValue), Arg.Any<CancellationToken>()).Returns(_current);
        _credentials.FindByIdAsync(5, Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(_credential);
        _directory.GetRolesAsync(Arg.Any<Credential>(), Arg.Any<CancellationToken>()).Returns(["admin"]);
        _issuer.Issue(Arg.Any<Credential>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<Guid>())
            .Returns(new AccessToken("jwt-baru", _time.GetUtcNow().AddMinutes(15)));
        _unitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<CancellationToken, Task>>()(CancellationToken.None));
    }

    private Task<NexUS.Auth.Application.Login.LoginResult> Refresh(string? token) =>
        new RefreshTokenCommandHandler(_tokens, _credentials, _issuer, _directory, _blacklist, _unitOfWork, _time,
                NullLogger<RefreshTokenCommandHandler>.Instance)
            .Handle(new RefreshTokenCommand(token, "ip", "ua"), CancellationToken.None);

    [Fact]
    public async Task Refresh_berhasil_merotasi_token_dan_menerbitkan_pasangan_baru()
    {
        RefreshToken? added = null;
        await _tokens.AddAsync(Arg.Do<RefreshToken>(t => added = t), Arg.Any<CancellationToken>());

        var result = await Refresh(_tokenValue);

        result.AccessToken.Should().Be("jwt-baru");
        result.RefreshToken.Should().NotBe(_tokenValue);
        _current.IsRevoked.Should().BeTrue();
        added!.TokenHash.Should().Be(RefreshTokenSecret.Hash(result.RefreshToken));
        added.FamilyId.Should().Be(_current.FamilyId);
    }

    [Fact]
    public async Task Pemakaian_ulang_token_lama_setelah_masa_tenggang_mencabut_keluarga_dan_mencatat_audit()
    {
        _current.Rotate("pengganti", _time.GetUtcNow(), null, null);
        _time.Advance(RefreshToken.ReuseGracePeriod + TimeSpan.FromSeconds(1));
        _tokens.RevokeFamilyAsync(default, default, default, default, default).ReturnsForAnyArgs(2);

        var ex = await FluentActions.Awaiting(() => Refresh(_tokenValue)).Should().ThrowAsync<RefreshRejectedException>();

        ex.Which.ClearCookie.Should().BeTrue();
        await _tokens.Received(1).RevokeFamilyAsync(5, _current.FamilyId, Arg.Any<DateTimeOffset>(),
            RefreshTokenRevocationReason.ReuseDetected, Arg.Any<CancellationToken>());
        _credential.DomainEvents.Should().ContainSingle(e => e is TokenRevokedDomainEvent
            && ((TokenRevokedDomainEvent)e).Reason == RefreshTokenRevocationReason.ReuseDetected);
        await _blacklist.Received(1).RevokeSessionAsync(5, _current.FamilyId, Arg.Any<CancellationToken>());
        _issuer.DidNotReceiveWithAnyArgs().Issue(default!, default!, default);
    }

    [Fact]
    public async Task Pemakaian_ulang_dalam_masa_tenggang_dianggap_refresh_bersamaan_tanpa_mencabut_keluarga()
    {
        _current.Rotate("pengganti", _time.GetUtcNow(), null, null);
        _time.Advance(TimeSpan.FromSeconds(5));

        var ex = await FluentActions.Awaiting(() => Refresh(_tokenValue)).Should().ThrowAsync<RefreshRejectedException>();

        ex.Which.ClearCookie.Should().BeFalse("cookie klien mungkin sudah diganti token baru oleh tab lain");
        await _tokens.DidNotReceiveWithAnyArgs().RevokeFamilyAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task Token_kedaluwarsa_401_tanpa_mencabut_keluarga()
    {
        _time.Advance(RefreshToken.Lifetime);

        await FluentActions.Awaiting(() => Refresh(_tokenValue)).Should().ThrowAsync<UnauthorizedException>();

        await _tokens.DidNotReceiveWithAnyArgs().RevokeFamilyAsync(default, default, default, default, default);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bukan-token")]
    public async Task Cookie_kosong_atau_rusak_401_tanpa_menyentuh_database(string? token)
    {
        await FluentActions.Awaiting(() => Refresh(token)).Should().ThrowAsync<UnauthorizedException>();

        await _tokens.DidNotReceiveWithAnyArgs().FindByHashAsync(default, default!, default);
    }

    [Fact]
    public async Task Token_tidak_dikenal_401()
    {
        await FluentActions.Awaiting(() => Refresh(RefreshTokenSecret.Generate(5))).Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task Kredensial_terkunci_tidak_mengeluarkan_sesi_yang_sudah_ada()
    {
        // Lockout bisa dipicu siapa pun yang tahu email korban; sesi aktif korban tidak boleh ikut hilang.
        for (var i = 0; i < Credential.MaxFailedAttempts; i++) _credential.RegisterFailedLogin(_time.GetUtcNow());

        (await Refresh(_tokenValue)).AccessToken.Should().Be("jwt-baru");
    }

    [Fact]
    public async Task Rotasi_tidak_tersimpan_bila_penerbitan_access_token_gagal()
    {
        _issuer.Issue(Arg.Any<Credential>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<Guid>())
            .Returns(_ => throw new InvalidOperationException("kunci rusak"));

        await FluentActions.Awaiting(() => Refresh(_tokenValue)).Should().ThrowAsync<InvalidOperationException>();

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        _current.IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task Refresh_bersamaan_yang_kalah_401_tanpa_mencabut_keluarga()
    {
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns<int>(_ => throw new ConcurrencyException("bentrok"));

        var ex = await FluentActions.Awaiting(() => Refresh(_tokenValue)).Should().ThrowAsync<RefreshRejectedException>();

        ex.Which.ClearCookie.Should().BeFalse();
        await _tokens.DidNotReceiveWithAnyArgs().RevokeFamilyAsync(default, default, default, default, default);
    }
}
