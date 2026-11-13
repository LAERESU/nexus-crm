using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using NexUS.BuildingBlocks.Auth;
using StackExchange.Redis;

namespace NexUS.User.IntegrationTests;

[Collection(OracleCollection.Name)]
[Trait("Category", "Oracle")]
public class PermissionPolicyTests(OracleUserFixture fixture)
{
    private sealed record UserBody(Guid Id, string Email, long RowVersion);

    private sealed record RoleBody(Guid Id, string Code, List<string> Permissions, long RowVersion);

    private sealed record RolePage(List<RoleBody> Items);

    private sealed record Granted(string Code, string ScopeType, Guid? ScopeId);

    private sealed record PermissionsBody(Guid UserId, List<Granted> Permissions);

    private static long Tenant => OracleUserFixture.TenantA;

    private HttpClient Admin => fixture.ClientAs(Tenant);

    private async Task<UserBody> CreateUserAsync()
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/users", new { email = $"perm-{Guid.NewGuid():N}@contoh.com", fullName = "Pengguna Uji" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<UserBody>())!;
    }

    private async Task<RoleBody> RoleAsync(string code) =>
        (await Admin.GetFromJsonAsync<RolePage>($"/api/v1/roles?search={code}&size=100"))!.Items.Single(r => r.Code == code);

    private Task<HttpResponseMessage> SetRolesAsync(Guid userId, params Guid[] roleIds) =>
        Admin.PutAsJsonAsync($"/api/v1/users/{userId}/roles", new { roles = roleIds.Select(id => new { roleId = id, scopeType = "global" }) });

    private static Task<HttpResponseMessage> TryManageAsync(HttpClient client) =>
        client.PostAsJsonAsync("/api/v1/users", new { email = $"coba-{Guid.NewGuid():N}@contoh.com", fullName = "Coba" });

    [Fact]
    public async Task User_tanpa_role_dan_user_tidak_dikenal_ditolak_di_endpoint_berpermission()
    {
        var noRoles = await CreateUserAsync();

        (await fixture.ClientAs(Tenant, noRoles.Id).GetAsync("/api/v1/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await TryManageAsync(fixture.ClientAs(Tenant, noRoles.Id))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await TryManageAsync(fixture.ClientAs(Tenant, Guid.NewGuid()))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Endpoint_hanya_login_tetap_terbuka_bagi_user_tanpa_role()
    {
        var noRoles = await CreateUserAsync();
        var client = fixture.ClientAs(Tenant, noRoles.Id);

        (await client.GetAsync("/api/v1/users/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        var own = await client.GetFromJsonAsync<PermissionsBody>($"/api/v1/users/{noRoles.Id}/permissions");
        own!.Permissions.Should().BeEmpty();
        (await client.GetFromJsonAsync<PermissionsBody>("/api/v1/users/me/permissions"))!.Permissions.Should().BeEmpty();

        // Aplikasi web membaca permission sendiri lewat /me/permissions (endpoint {id}/permissions diblokir Gateway).
        var admin = await fixture.ClientAs(Tenant).GetFromJsonAsync<PermissionsBody>("/api/v1/users/me/permissions");
        admin!.Permissions.Select(p => p.Code).Should().Contain("user.manage");
    }

    [Fact]
    public async Task Pemberian_dan_pencabutan_role_langsung_berlaku_tanpa_menunggu_ttl()
    {
        var user = await CreateUserAsync();
        var client = fixture.ClientAs(Tenant, user.Id);
        var tenantAdmin = await RoleAsync("TENANT_ADMIN");

        (await TryManageAsync(client)).StatusCode.Should().Be(HttpStatusCode.Forbidden); // mengisi cache dengan daftar kosong

        (await SetRolesAsync(user.Id, tenantAdmin.Id)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TryManageAsync(client)).StatusCode.Should().Be(HttpStatusCode.Created);

        (await SetRolesAsync(user.Id)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TryManageAsync(client)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Perubahan_permission_role_langsung_berlaku_untuk_semua_pemegangnya()
    {
        var create = await Admin.PostAsJsonAsync("/api/v1/roles",
            new { code = $"P_{Guid.NewGuid():N}"[..14].ToUpperInvariant(), name = "Peran Uji", permissions = new[] { "user.manage" } });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var role = (await create.Content.ReadFromJsonAsync<RoleBody>())!;
        var holders = new[] { await CreateUserAsync(), await CreateUserAsync() };
        foreach (var holder in holders) (await SetRolesAsync(holder.Id, role.Id)).StatusCode.Should().Be(HttpStatusCode.OK);
        foreach (var holder in holders) (await TryManageAsync(fixture.ClientAs(Tenant, holder.Id))).StatusCode.Should().Be(HttpStatusCode.Created);

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/roles/{role.Id}/permissions")
        {
            Content = JsonContent.Create(new { permissions = new[] { "user.read" } }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{role.RowVersion}\"");
        (await Admin.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);

        foreach (var holder in holders)
            (await TryManageAsync(fixture.ClientAs(Tenant, holder.Id))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Permission_disimpan_di_redis_dengan_key_tenant_dan_ttl_maksimal_15_menit()
    {
        var user = await CreateUserAsync();
        (await fixture.ClientAs(Tenant, user.Id).GetAsync("/api/v1/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var key = (RedisKey)$"tnt:{Tenant}:perm:{user.Id:D}";
        var database = fixture.Redis.GetDatabase();
        (await database.KeyExistsAsync(key)).Should().BeTrue();
        (await database.KeyTimeToLiveAsync(key)).Should().BeLessThanOrEqualTo(PermissionCacheKeys.Ttl).And.BeGreaterThan(TimeSpan.Zero);

        var member = await RoleAsync("MEMBER");
        (await SetRolesAsync(user.Id, member.Id)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await database.KeyExistsAsync(key)).Should().BeFalse("perubahan role menghapus cache user itu");
    }

    [Fact]
    public async Task Menonaktifkan_user_langsung_mencabut_akses_dan_mengaktifkan_memulihkannya()
    {
        var user = await CreateUserAsync();
        var tenantAdmin = await RoleAsync("TENANT_ADMIN");
        (await SetRolesAsync(user.Id, tenantAdmin.Id)).StatusCode.Should().Be(HttpStatusCode.OK);
        var client = fixture.ClientAs(Tenant, user.Id);
        (await TryManageAsync(client)).StatusCode.Should().Be(HttpStatusCode.Created);

        var current = await Admin.GetFromJsonAsync<UserBody>($"/api/v1/users/{user.Id}");
        var deactivate = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/users/{user.Id}/status") { Content = JsonContent.Create(new { status = "inactive" }) };
        deactivate.Headers.TryAddWithoutValidation("If-Match", $"\"{current!.RowVersion}\"");
        var deactivated = await Admin.SendAsync(deactivate);
        deactivated.StatusCode.Should().Be(HttpStatusCode.OK);
        (await TryManageAsync(client)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var version = (await deactivated.Content.ReadFromJsonAsync<UserBody>())!.RowVersion;
        var activate = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/users/{user.Id}/status") { Content = JsonContent.Create(new { status = "active" }) };
        activate.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        (await Admin.SendAsync(activate)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TryManageAsync(client)).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Role_bersope_departemen_tidak_cukup_untuk_endpoint_kelola_tenant()
    {
        var user = await CreateUserAsync();
        var department = await (await Admin.PostAsJsonAsync("/api/v1/departments", new { code = $"D{Guid.NewGuid():N}"[..12], name = "Dept Uji" }))
            .Content.ReadFromJsonAsync<UserBody>();
        var tenantAdmin = await RoleAsync("TENANT_ADMIN");
        var assign = await Admin.PutAsJsonAsync($"/api/v1/users/{user.Id}/roles",
            new { roles = new[] { new { roleId = tenantAdmin.Id, scopeType = "department", scopeId = department!.Id } } });
        assign.StatusCode.Should().Be(HttpStatusCode.OK);

        (await TryManageAsync(fixture.ClientAs(Tenant, user.Id))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_tenant_lain_tidak_membawa_permission_ke_tenant_ini()
    {
        // ID admin tenant A dipakai dengan klaim tenant B: key cache dan query dipisah per tenant, jadi tidak punya permission.
        var crossTenant = fixture.ClientAs(OracleUserFixture.TenantB, OracleUserFixture.AdminId(Tenant));

        (await TryManageAsync(crossTenant)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_seed_dev_memegang_user_manage()
    {
        var devAdmin = Guid.Parse("6f1c2a5e-0d3b-4c8e-9a71-2b4d5e6f7a80");

        var permissions = await fixture.ClientAs(Tenant, devAdmin).GetFromJsonAsync<PermissionsBody>($"/api/v1/users/{devAdmin}/permissions");

        permissions!.Permissions.Select(p => p.Code).Should().Contain("user.manage");
    }
}
