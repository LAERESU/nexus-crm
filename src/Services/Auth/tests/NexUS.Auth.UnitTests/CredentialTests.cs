using FluentAssertions;
using NexUS.Auth.Domain.Credentials;
using NexUS.BuildingBlocks.Common.Exceptions;

namespace NexUS.Auth.UnitTests;

public class CredentialTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 8, 0, 0, TimeSpan.Zero);

    private static Credential NewCredential() => Credential.Create(1, Guid.NewGuid(), "  Budi@Demo.Local ", "hash");

    [Fact]
    public void Email_dinormalisasi_huruf_kecil_tanpa_spasi()
    {
        NewCredential().Email.Should().Be("budi@demo.local");
    }

    [Fact]
    public void Lima_kali_gagal_dalam_15_menit_mengunci_akun_15_menit()
    {
        var credential = NewCredential();

        for (var i = 0; i < Credential.MaxFailedAttempts - 1; i++) credential.RegisterFailedLogin(Now.AddMinutes(i));
        credential.IsLocked(Now.AddMinutes(4)).Should().BeFalse();

        credential.RegisterFailedLogin(Now.AddMinutes(4));

        credential.IsLocked(Now.AddMinutes(4)).Should().BeTrue();
        credential.IsLocked(Now.AddMinutes(4) + Credential.LockDuration - TimeSpan.FromSeconds(1)).Should().BeTrue();
        credential.IsLocked(Now.AddMinutes(4) + Credential.LockDuration).Should().BeFalse();
        credential.DomainEvents.Should().ContainSingle(e => e is CredentialLockedDomainEvent);
    }

    [Fact]
    public void Kegagalan_di_luar_jendela_15_menit_dihitung_ulang()
    {
        var credential = NewCredential();
        for (var i = 0; i < Credential.MaxFailedAttempts - 1; i++) credential.RegisterFailedLogin(Now);

        credential.RegisterFailedLogin(Now + Credential.FailureWindow + TimeSpan.FromMinutes(1));

        credential.FailedAttempts.Should().Be(1);
        credential.IsLocked(Now + Credential.FailureWindow + TimeSpan.FromMinutes(1)).Should().BeFalse();
    }

    [Fact]
    public void Login_berhasil_mereset_hitungan_gagal_dan_memicu_event()
    {
        var credential = NewCredential();
        credential.RegisterFailedLogin(Now);
        credential.RegisterFailedLogin(Now);

        credential.RegisterSuccessfulLogin(Now);

        credential.FailedAttempts.Should().Be(0);
        credential.FirstFailedAt.Should().BeNull();
        credential.DomainEvents.Should().ContainSingle(e => e is UserLoggedInDomainEvent);
    }

    [Fact]
    public void Login_berhasil_ditolak_saat_akun_terkunci()
    {
        var credential = NewCredential();
        for (var i = 0; i < Credential.MaxFailedAttempts; i++) credential.RegisterFailedLogin(Now);

        var act = () => credential.RegisterSuccessfulLogin(Now);

        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Tenant_wajib_diisi(long tenantId)
    {
        var act = () => Credential.Create(tenantId, Guid.NewGuid(), "a@b.c", "hash");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Refresh_token_berlaku_7_hari()
    {
        var credential = NewCredential();

        var token = RefreshToken.Issue(credential, "abc", Now, "browser", "10.0.0.1");

        token.ExpiresAt.Should().Be(Now.AddDays(7));
        token.IsActive(Now.AddDays(7).AddSeconds(-1)).Should().BeTrue();
        token.IsActive(Now.AddDays(7)).Should().BeFalse();
    }
}
