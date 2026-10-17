using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NexUS.Auth.Application.Abstractions;
using NexUS.Auth.Domain.Credentials;
using NexUS.Auth.Infrastructure.Persistence;

namespace NexUS.Auth.IntegrationTests;

/// <summary>Pemetaan EF Core ke Oracle diuji langsung, sehingga kegagalan menampilkan pesan ORA- aslinya.</summary>
[Collection(OracleCollection.Name)]
[Trait("Category", "Oracle")]
public class PersistenceTests(OracleAuthFixture fixture)
{
    private async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private async Task<Credential> NewCredentialAsync(string email) => await InScope(async sp =>
    {
        var db = sp.GetRequiredService<AuthDbContext>();
        var credential = Credential.Create(1, Guid.NewGuid(), email, "hash");
        db.Credentials.Add(credential);
        await db.SaveChangesAsync();
        return credential;
    });

    [Fact]
    public async Task Seed_admin_dev_tersimpan()
    {
        var count = await InScope(sp => sp.GetRequiredService<AuthDbContext>().Credentials
            .IgnoreQueryFilters().CountAsync(c => c.Email == OracleAuthFixture.AdminEmail));

        count.Should().Be(1);
    }

    [Fact]
    public async Task FindByEmail_dengan_filter_soft_delete_menemukan_kredensial()
    {
        var created = await NewCredentialAsync("cari.email@demo.local");

        var found = await InScope(sp => sp.GetRequiredService<ICredentialRepository>()
            .FindByEmailAsync("cari.email@demo.local", CancellationToken.None));

        found.Should().NotBeNull();
        found!.PublicId.Should().Be(created.PublicId);
        found.UserId.Should().Be(created.UserId);
    }

    [Fact]
    public async Task Update_kredensial_menaikkan_row_version()
    {
        await NewCredentialAsync("update.rv@demo.local");

        var rowVersion = await InScope(async sp =>
        {
            var db = sp.GetRequiredService<AuthDbContext>();
            var credential = await db.Credentials.SingleAsync(c => c.Email == "update.rv@demo.local");
            credential.RegisterFailedLogin(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
            return credential.RowVersion;
        });

        rowVersion.Should().Be(1);
    }

    [Fact]
    public async Task Riwayat_login_tanpa_tenant_dan_dengan_tenant_bisa_disimpan()
    {
        var credential = await NewCredentialAsync("riwayat@demo.local");

        var saved = await InScope(async sp =>
        {
            var db = sp.GetRequiredService<AuthDbContext>();
            var tracked = await db.Credentials.SingleAsync(c => c.Email == "riwayat@demo.local");
            db.LoginAttempts.Add(LoginAttempt.Failure(null, LoginFailureReason.UnknownEmail, DateTimeOffset.UtcNow, "10.0.0.1", "ua"));
            db.LoginAttempts.Add(LoginAttempt.Success(tracked, DateTimeOffset.UtcNow, "10.0.0.1", "ua"));
            return await db.SaveChangesAsync();
        });

        saved.Should().Be(2);
        var readBack = await InScope(sp => sp.GetRequiredService<AuthDbContext>().LoginAttempts
            .Where(a => a.TenantId == 0 || a.CredentialId == credential.Id).ToListAsync());
        readBack.Should().Contain(a => a.TenantId == 0 && a.FailureReason == LoginFailureReason.UnknownEmail);
        readBack.Should().Contain(a => a.Succeeded && a.CredentialId == credential.Id);
    }

    [Fact]
    public async Task Refresh_token_dan_outbox_tersimpan()
    {
        await NewCredentialAsync("token.outbox@demo.local");

        var saved = await InScope(async sp =>
        {
            var db = sp.GetRequiredService<AuthDbContext>();
            var credential = await db.Credentials.SingleAsync(c => c.Email == "token.outbox@demo.local");
            credential.RegisterSuccessfulLogin(DateTimeOffset.UtcNow); // memicu event → outbox
            db.RefreshTokens.Add(RefreshToken.Issue(credential, RefreshTokenSecret.Hash(Guid.NewGuid().ToString()), DateTimeOffset.UtcNow, "ua", "10.0.0.1"));
            return await db.SaveChangesAsync();
        });

        saved.Should().BeGreaterThanOrEqualTo(3); // update kredensial + refresh token + outbox
    }

    [Fact]
    public async Task Role_sementara_dibaca_dari_kolom_ROLES()
    {
        var roles = await InScope(async sp =>
        {
            var db = sp.GetRequiredService<AuthDbContext>();
            var admin = await db.Credentials.SingleAsync(c => c.Email == OracleAuthFixture.AdminEmail);
            return await sp.GetRequiredService<IUserDirectory>().GetRolesAsync(admin, CancellationToken.None);
        });

        roles.Should().Contain("admin");
    }
}
