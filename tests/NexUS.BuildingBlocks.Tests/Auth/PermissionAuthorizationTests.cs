using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NexUS.BuildingBlocks.Auth;
using NSubstitute;

namespace NexUS.BuildingBlocks.Tests.Auth;

public class PermissionHandlerTests
{
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private PermissionRequirement _requirement = new("task.update");

    private static ClaimsPrincipal Principal(Guid? user, string? tenant = "7") =>
        new(new ClaimsIdentity(
            new[] { user is { } id ? new Claim("sub", id.ToString()) : null, tenant is null ? null : new Claim("tid", tenant) }
                .OfType<Claim>(), authenticationType: "Test"));

    private async Task<bool> EvaluateAsync(ClaimsPrincipal principal)
    {
        var context = new AuthorizationHandlerContext([_requirement], principal, resource: null);
        var services = new ServiceCollection().AddSingleton(_permissions).BuildServiceProvider();
        await new PermissionHandler(services, NullLogger<PermissionHandler>.Instance).HandleAsync(context);
        return context.HasSucceeded;
    }

    [Fact]
    public async Task Berhasil_bila_user_memegang_permission_pada_scope_mana_pun()
    {
        var user = Guid.NewGuid();
        _permissions.GetAsync(7, user, Arg.Any<CancellationToken>())
            .Returns([new GrantedPermission("task.read", "global", null), new GrantedPermission("task.update", "project", Guid.NewGuid())]);

        (await EvaluateAsync(Principal(user))).Should().BeTrue();
    }

    [Fact]
    public async Task Gagal_bila_permission_tidak_dimiliki_atau_user_tanpa_role()
    {
        var user = Guid.NewGuid();
        _permissions.GetAsync(7, user, Arg.Any<CancellationToken>()).Returns([new GrantedPermission("task.read", "global", null)]);
        (await EvaluateAsync(Principal(user))).Should().BeFalse();

        _permissions.GetAsync(7, user, Arg.Any<CancellationToken>()).Returns([]);
        (await EvaluateAsync(Principal(user))).Should().BeFalse();
    }

    [Fact]
    public async Task Gagal_tanpa_klaim_sub_atau_tid_tanpa_memanggil_sumber()
    {
        (await EvaluateAsync(Principal(user: null))).Should().BeFalse();
        (await EvaluateAsync(Principal(Guid.NewGuid(), tenant: null))).Should().BeFalse();
        (await EvaluateAsync(Principal(Guid.NewGuid(), tenant: "bukan-angka"))).Should().BeFalse();
        (await EvaluateAsync(new ClaimsPrincipal(new ClaimsIdentity()))).Should().BeFalse();

        await _permissions.DidNotReceiveWithAnyArgs().GetAsync(default, default, default);
    }

    [Fact]
    public async Task Requirement_global_mengabaikan_permission_bersope_departemen_atau_proyek()
    {
        _requirement = new PermissionRequirement("user.manage", globalOnly: true);
        var user = Guid.NewGuid();
        _permissions.GetAsync(7, user, Arg.Any<CancellationToken>()).Returns([new GrantedPermission("user.manage", "department", Guid.NewGuid())]);
        (await EvaluateAsync(Principal(user))).Should().BeFalse();

        _permissions.GetAsync(7, user, Arg.Any<CancellationToken>()).Returns([new GrantedPermission("user.manage", "global", null)]);
        (await EvaluateAsync(Principal(user))).Should().BeTrue();
    }

    [Fact]
    public async Task Fail_closed_juga_bila_sumber_timeout()
    {
        _permissions.GetAsync(Arg.Any<long>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<GrantedPermission>>(_ => throw new TaskCanceledException("timeout HttpClient"));

        (await EvaluateAsync(Principal(Guid.NewGuid()))).Should().BeFalse();
    }

    [Fact]
    public async Task Fail_closed_bila_sumber_permission_error()
    {
        _permissions.GetAsync(Arg.Any<long>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<GrantedPermission>>(_ => throw new HttpRequestException("User Service mati"));

        (await EvaluateAsync(Principal(Guid.NewGuid()))).Should().BeFalse();
    }
}

public class PermissionPolicyProviderTests
{
    private static PermissionPolicyProvider Provider(Action<AuthorizationOptions>? configure = null)
    {
        var options = new AuthorizationOptions();
        configure?.Invoke(options);
        return new PermissionPolicyProvider(Options.Create(options));
    }

    [Theory]
    [InlineData("user.manage")]
    [InlineData("sop.category.manage")]
    public async Task Nama_modul_aksi_menjadi_policy_dengan_syarat_permission(string name)
    {
        var policy = await Provider().GetPolicyAsync(name);

        policy.Should().NotBeNull();
        policy!.Requirements.OfType<PermissionRequirement>().Should().ContainSingle().Which.Permission.Should().Be(name);
        policy.Requirements.Should().ContainItemsAssignableTo<IAuthorizationRequirement>()
            .And.Contain(r => r.GetType().Name == "DenyAnonymousAuthorizationRequirement");
        (await Provider().GetPolicyAsync(name)).Should().NotBeNull();
    }

    [Theory]
    [InlineData("User.Manage")]
    [InlineData("usermanage")]
    [InlineData("user.")]
    [InlineData(".manage")]
    [InlineData("user manage")]
    [InlineData("")]
    public async Task Nama_selain_modul_aksi_tidak_dikenal(string name)
    {
        (await Provider().GetPolicyAsync(name)).Should().BeNull();
    }

    [Fact]
    public async Task Policy_terdaftar_didahulukan_atas_policy_dinamis()
    {
        var provider = Provider(o => o.AddPolicy("user.manage", p => p.RequireRole("khusus")));

        var policy = await provider.GetPolicyAsync("user.manage");

        policy!.Requirements.OfType<PermissionRequirement>().Should().BeEmpty();
    }
}

public class PermissionServiceTests
{
    private readonly IPermissionCache _cache = Substitute.For<IPermissionCache>();
    private readonly IPermissionSource _source = Substitute.For<IPermissionSource>();
    private readonly Guid _user = Guid.NewGuid();

    [Fact]
    public async Task Cache_hit_tidak_menyentuh_sumber()
    {
        IReadOnlyList<GrantedPermission> cached = [new("task.read", "global", null)];
        _cache.GetAsync(7, _user, Arg.Any<CancellationToken>()).Returns(cached);

        (await new PermissionService(_cache, _source).GetAsync(7, _user)).Should().BeEquivalentTo(cached);

        await _source.DidNotReceiveWithAnyArgs().GetAsync(default, default, default);
    }

    [Fact]
    public async Task Cache_miss_mengambil_dari_sumber_lalu_menyimpan_termasuk_daftar_kosong()
    {
        _cache.GetAsync(7, _user, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<GrantedPermission>?)null);
        _source.GetAsync(7, _user, Arg.Any<CancellationToken>()).Returns([]);

        (await new PermissionService(_cache, _source).GetAsync(7, _user)).Should().BeEmpty();

        await _cache.Received(1).SetAsync(7, _user, Arg.Is<IReadOnlyList<GrantedPermission>>(p => p.Count == 0), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Error_sumber_diteruskan_dan_tidak_disimpan_ke_cache()
    {
        _cache.GetAsync(7, _user, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<GrantedPermission>?)null);
        _source.GetAsync(7, _user, Arg.Any<CancellationToken>()).Returns<IReadOnlyList<GrantedPermission>>(_ => throw new HttpRequestException());

        await FluentActions.Awaiting(() => new PermissionService(_cache, _source).GetAsync(7, _user)).Should().ThrowAsync<HttpRequestException>();

        await _cache.DidNotReceiveWithAnyArgs().SetAsync(default, default, default!, default);
    }

    [Fact]
    public void Key_cache_berawalan_tenant_dan_ttl_15_menit()
    {
        PermissionCacheKeys.For(7, _user).Should().Be($"tnt:7:perm:{_user:D}");
        PermissionCacheKeys.Ttl.Should().Be(TimeSpan.FromMinutes(15));
    }
}
