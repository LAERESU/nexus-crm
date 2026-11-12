using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexUS.User.Domain.Roles;
using NexUS.User.Infrastructure.Seeding;

namespace NexUS.User.IntegrationTests;

[Collection(OracleCollection.Name)]
[Trait("Category", "Oracle")]
public class RbacEndpointTests(OracleUserFixture fixture)
{
    private sealed record RoleBody(Guid Id, string Code, string Name, string? Description, bool IsSystem, List<string> Permissions, long RowVersion);

    private sealed record RolePage(List<RoleBody> Items, int Page, int Size, long TotalCount);

    private sealed record UserBody(Guid Id, string Email, long RowVersion);

    private sealed record AssignedRole(Guid RoleId, string RoleCode, string ScopeType, Guid? ScopeId);

    private sealed record Granted(string Code, string ScopeType, Guid? ScopeId);

    private sealed record UserPermissionsBody(Guid UserId, List<AssignedRole> Roles, List<Granted> Permissions);

    private sealed record DepartmentBody(Guid Id);

    private HttpClient Admin(long tenant = OracleUserFixture.TenantA) => fixture.ClientAs(tenant);

    private static string NewCode() => $"R_{Guid.NewGuid():N}"[..14].ToUpperInvariant();

    private static HttpRequestMessage WithVersion(HttpMethod method, string url, long version, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        return request;
    }

    private static async Task<RoleBody> CreateRoleAsync(HttpClient client, string[]? permissions = null, string? code = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/roles",
            new { code = code ?? NewCode(), name = "Peran Uji", description = "uji", permissions = permissions ?? [] });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<RoleBody>())!;
    }

    private static async Task<UserBody> CreateUserAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/users", new { email = $"rbac-{Guid.NewGuid():N}@contoh.com", fullName = "Pengguna Uji" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<UserBody>())!;
    }

    private static Task<HttpResponseMessage> AssignAsync(HttpClient client, Guid userId, params object[] roles) =>
        client.PutAsJsonAsync($"/api/v1/users/{userId}/roles", new { roles });

    private async Task<int> OutboxCountAsync(string typeFragment, Guid idInPayload) =>
        Convert.ToInt32(await fixture.ScalarAsync(
            $"SELECT COUNT(*) FROM OUTBOX_MESSAGES WHERE TYPE LIKE '%{typeFragment}%' AND PAYLOAD LIKE '%{idInPayload:D}%'"));

    [Fact]
    public async Task Setiap_tenant_memiliki_role_bawaan_sistem_dengan_permission_dari_katalog()
    {
        foreach (var tenant in new[] { OracleUserFixture.TenantA, OracleUserFixture.TenantB })
        {
            var page = await Admin(tenant).GetFromJsonAsync<RolePage>("/api/v1/roles?size=100");

            var system = page!.Items.Where(r => r.IsSystem).ToList();
            system.Select(r => r.Code).Should().BeEquivalentTo(DefaultRoleCatalog.Roles.Select(r => r.Code));
            foreach (var definition in DefaultRoleCatalog.Roles)
                system.Single(r => r.Code == definition.Code).Permissions.Should().BeEquivalentTo(definition.Permissions);
        }
    }

    [Fact]
    public async Task Katalog_permission_dapat_dibaca_pemegang_role_read_saja()
    {
        (await fixture.MemberClient(OracleUserFixture.TenantA).GetAsync("/api/v1/permissions")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var catalog = await Admin().GetFromJsonAsync<List<PermissionBody>>("/api/v1/permissions");

        catalog!.Select(p => p.Code).Should().BeEquivalentTo(DefaultRoleCatalog.Permissions.Select(p => p.Code));
        catalog.Should().OnlyContain(p => p.Module.Length > 0 && p.Code.StartsWith(p.Module + "."));
    }

    private sealed record PermissionBody(string Code, string Module, string? Description);

    [Fact]
    public async Task Role_sistem_tidak_bisa_dihapus_diubah_atau_diganti_permission_nya()
    {
        var client = Admin();
        var page = await client.GetFromJsonAsync<RolePage>("/api/v1/roles?size=100");
        var system = page!.Items.First(r => r.IsSystem);

        (await client.SendAsync(WithVersion(HttpMethod.Delete, $"/api/v1/roles/{system.Id}", system.RowVersion)))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await client.SendAsync(WithVersion(HttpMethod.Put, $"/api/v1/roles/{system.Id}", system.RowVersion, new { name = "Ganti" })))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await client.SendAsync(WithVersion(HttpMethod.Put, $"/api/v1/roles/{system.Id}/permissions", system.RowVersion, new { permissions = new[] { "task.read" } })))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        (await client.GetFromJsonAsync<RoleBody>($"/api/v1/roles/{system.Id}"))!.Name.Should().Be(system.Name);
    }

    [Fact]
    public async Task Role_kustom_dapat_dibuat_diubah_dan_dihapus_dengan_If_Match()
    {
        var client = Admin();
        var role = await CreateRoleAsync(client, ["task.read"]);
        role.IsSystem.Should().BeFalse();
        role.Permissions.Should().Equal("task.read");

        var updated = await client.SendAsync(WithVersion(HttpMethod.Put, $"/api/v1/roles/{role.Id}", role.RowVersion, new { name = "Nama Baru", description = "baru" }));
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await updated.Content.ReadFromJsonAsync<RoleBody>();
        after!.Name.Should().Be("Nama Baru");
        after.RowVersion.Should().BeGreaterThan(role.RowVersion);

        var stale = await client.SendAsync(WithVersion(HttpMethod.Put, $"/api/v1/roles/{role.Id}", role.RowVersion, new { name = "Basi" }));
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await client.SendAsync(WithVersion(HttpMethod.Delete, $"/api/v1/roles/{role.Id}", after.RowVersion))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/v1/roles/{role.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Kode_role_unik_per_tenant_permission_asing_ditolak_dan_kode_bekas_hapus_bisa_dipakai_lagi()
    {
        var client = Admin();
        var code = NewCode();
        var role = await CreateRoleAsync(client, code: code);

        (await client.PostAsJsonAsync("/api/v1/roles", new { code = code.ToLowerInvariant(), name = "Dobel" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await client.PostAsJsonAsync("/api/v1/roles", new { code = NewCode(), name = "Asing", permissions = new[] { "tidak.ada" } }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await client.PostAsJsonAsync("/api/v1/roles", new { code = "kode salah", name = "Salah" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        (await Admin(OracleUserFixture.TenantB).PostAsJsonAsync("/api/v1/roles", new { code, name = "Tenant lain" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        (await client.SendAsync(WithVersion(HttpMethod.Delete, $"/api/v1/roles/{role.Id}", role.RowVersion))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.PostAsJsonAsync("/api/v1/roles", new { code, name = "Pakai lagi" })).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Perubahan_role_langsung_tercermin_pada_endpoint_permissions()
    {
        var client = Admin();
        var user = await CreateUserAsync(client);
        var role = await CreateRoleAsync(client, ["task.read"]);

        (await AssignAsync(client, user.Id, new { roleId = role.Id, scopeType = "global" })).StatusCode.Should().Be(HttpStatusCode.OK);
        var before = await client.GetFromJsonAsync<UserPermissionsBody>($"/api/v1/users/{user.Id}/permissions");
        before!.Permissions.Select(p => p.Code).Should().Equal("task.read");

        var change = await client.SendAsync(WithVersion(HttpMethod.Put, $"/api/v1/roles/{role.Id}/permissions", role.RowVersion,
            new { permissions = new[] { "task.read", "task.create" } }));
        change.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await client.GetFromJsonAsync<UserPermissionsBody>($"/api/v1/users/{user.Id}/permissions");
        after!.Permissions.Select(p => p.Code).Should().BeEquivalentTo("task.read", "task.create");
        (await OutboxCountAsync("RoleChangedDomainEvent", role.Id)).Should().Be(1);

        // Mencabut role dari user langsung menghilangkan permission-nya.
        (await AssignAsync(client, user.Id)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<UserPermissionsBody>($"/api/v1/users/{user.Id}/permissions"))!.Permissions.Should().BeEmpty();
        (await OutboxCountAsync("RoleChangedDomainEvent", user.Id)).Should().Be(2); // penugasan, lalu pencabutan
    }

    [Fact]
    public async Task Set_permission_dengan_versi_basi_409_dan_tanpa_perubahan_tidak_menerbitkan_event()
    {
        var client = Admin();
        var role = await CreateRoleAsync(client, ["task.read"]);

        var same = await client.SendAsync(WithVersion(HttpMethod.Put, $"/api/v1/roles/{role.Id}/permissions", role.RowVersion, new { permissions = new[] { "task.read" } }));
        same.StatusCode.Should().Be(HttpStatusCode.OK);
        (await OutboxCountAsync("RoleChangedDomainEvent", role.Id)).Should().Be(0);

        var first = await client.SendAsync(WithVersion(HttpMethod.Put, $"/api/v1/roles/{role.Id}/permissions", role.RowVersion, new { permissions = new[] { "task.create" } }));
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var stale = await client.SendAsync(WithVersion(HttpMethod.Put, $"/api/v1/roles/{role.Id}/permissions", role.RowVersion, new { permissions = new[] { "task.delete" } }));
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Role_yang_masih_ditugaskan_tidak_bisa_dihapus()
    {
        var client = Admin();
        var user = await CreateUserAsync(client);
        var role = await CreateRoleAsync(client);
        await AssignAsync(client, user.Id, new { roleId = role.Id, scopeType = "global" });

        var response = await client.SendAsync(WithVersion(HttpMethod.Delete, $"/api/v1/roles/{role.Id}", role.RowVersion));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Penugasan_role_dengan_scope_departemen_dicatat_bersama_scopenya()
    {
        var client = Admin();
        var user = await CreateUserAsync(client);
        var role = await CreateRoleAsync(client, ["project.read"]);
        var department = await (await client.PostAsJsonAsync("/api/v1/departments", new { code = $"D{Guid.NewGuid():N}"[..12], name = "Dept" }))
            .Content.ReadFromJsonAsync<DepartmentBody>();

        var response = await AssignAsync(client, user.Id,
            new { roleId = role.Id, scopeType = "global" },
            new { roleId = role.Id, scopeType = "department", scopeId = department!.Id });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UserPermissionsBody>();
        body!.Roles.Should().HaveCount(2);
        body.Permissions.Should().Contain(p => p.Code == "project.read" && p.ScopeType == "department" && p.ScopeId == department.Id);
        body.Permissions.Should().Contain(p => p.Code == "project.read" && p.ScopeType == "global" && p.ScopeId == null);
    }

    [Fact]
    public async Task Scope_tidak_valid_ditolak()
    {
        var client = Admin();
        var user = await CreateUserAsync(client);
        var role = await CreateRoleAsync(client);

        (await AssignAsync(client, user.Id, new { roleId = role.Id, scopeType = "global", scopeId = Guid.NewGuid() }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await AssignAsync(client, user.Id, new { roleId = role.Id, scopeType = "department" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await AssignAsync(client, user.Id, new { roleId = role.Id, scopeType = "department", scopeId = Guid.NewGuid() }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AssignAsync(client, user.Id, new { roleId = role.Id, scopeType = "galaksi" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AssignAsync(client, user.Id, new { roleId = Guid.NewGuid(), scopeType = "global" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Role_dan_user_tenant_lain_tidak_terlihat_dan_tidak_bisa_dipakai()
    {
        var roleA = await CreateRoleAsync(Admin(OracleUserFixture.TenantA));
        var userB = await CreateUserAsync(Admin(OracleUserFixture.TenantB));
        var tenantB = Admin(OracleUserFixture.TenantB);

        (await tenantB.GetAsync($"/api/v1/roles/{roleA.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AssignAsync(tenantB, userB.Id, new { roleId = roleA.Id, scopeType = "global" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Admin(OracleUserFixture.TenantA).GetAsync($"/api/v1/users/{userB.Id}/permissions")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task User_biasa_hanya_boleh_membaca_permission_miliknya_sendiri()
    {
        var admin = Admin();
        var user = await CreateUserAsync(admin);
        var other = await CreateUserAsync(admin);
        var asUser = fixture.ClientAs(OracleUserFixture.TenantA, userId: user.Id);

        (await asUser.GetAsync($"/api/v1/users/{user.Id}/permissions")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await asUser.GetAsync($"/api/v1/users/{other.Id}/permissions")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.GetAsync($"/api/v1/users/{other.Id}/permissions")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Tanpa_permission_role_manage_dan_role_read_endpoint_role_mengembalikan_403()
    {
        var member = fixture.MemberClient(OracleUserFixture.TenantA);

        (await member.PostAsJsonAsync("/api/v1/roles", new { code = NewCode(), name = "X" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.PutAsJsonAsync($"/api/v1/users/{Guid.NewGuid()}/roles", new { roles = Array.Empty<object>() })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.GetAsync("/api/v1/roles")).StatusCode.Should().Be(HttpStatusCode.Forbidden); // MEMBER tidak punya role.read
    }

    [Fact]
    public async Task Seeder_idempoten_dan_memulihkan_permission_role_sistem_yang_menyimpang()
    {
        var client = Admin();
        var member = (await client.GetFromJsonAsync<RolePage>("/api/v1/roles?search=MEMBER&size=100"))!.Items.Single(r => r.Code == "MEMBER");

        // Simulasi katalog bertambah: satu permission dicabut langsung di database, lalu seeder dijalankan ulang.
        await fixture.ScalarAsync(
            $"DELETE FROM ROLE_PERMISSIONS WHERE ROLE_ID = (SELECT ID FROM ROLES WHERE TENANT_ID = {OracleUserFixture.TenantA} AND CODE = 'MEMBER') " +
            "AND PERMISSION_ID = (SELECT ID FROM PERMISSIONS WHERE CODE = 'task.create')");
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var provisioner = scope.ServiceProvider.GetRequiredService<DefaultRoleProvisioner>();
            await provisioner.ProvisionAllAsync(default);
            await provisioner.ProvisionAllAsync(default); // kedua kali tidak mengubah apa pun
        }

        var restored = await client.GetFromJsonAsync<RoleBody>($"/api/v1/roles/{member.Id}");
        restored!.Permissions.Should().BeEquivalentTo(member.Permissions);
        Convert.ToInt32(await fixture.ScalarAsync($"SELECT COUNT(*) FROM ROLES WHERE TENANT_ID = {OracleUserFixture.TenantA} AND IS_SYSTEM = 1"))
            .Should().Be(DefaultRoleCatalog.Roles.Count);
    }

    [Fact]
    public async Task List_role_mendukung_paging_pencarian_dan_membatasi_size()
    {
        var client = Admin();
        (await client.GetAsync("/api/v1/roles?size=101")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var page = await client.GetFromJsonAsync<RolePage>("/api/v1/roles?page=1&size=3");
        page!.Items.Should().HaveCount(3);
        page.TotalCount.Should().BeGreaterThanOrEqualTo(DefaultRoleCatalog.Roles.Count);

        var found = await client.GetFromJsonAsync<RolePage>("/api/v1/roles?search=auditor");
        found!.Items.Should().ContainSingle(r => r.Code == "AUDITOR");
    }

    [Fact]
    public async Task Kode_role_bawaan_dicadangkan_dan_tidak_bisa_dipakai_role_kustom()
    {
        var response = await Admin().PostAsJsonAsync("/api/v1/roles", new { code = "auditor", name = "Palsu" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Put_permission_yang_sama_dengan_versi_basi_tetap_409()
    {
        var client = Admin();
        var role = await CreateRoleAsync(client, ["task.read"]);
        (await client.SendAsync(WithVersion(HttpMethod.Put, $"/api/v1/roles/{role.Id}/permissions", role.RowVersion, new { permissions = new[] { "task.create" } })))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Isinya sama dengan yang tersimpan sekarang (tidak ada perubahan), tetapi versi klien sudah basi.
        var stale = await client.SendAsync(WithVersion(HttpMethod.Put, $"/api/v1/roles/{role.Id}/permissions", role.RowVersion, new { permissions = new[] { "task.create" } }));

        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Role_yang_sudah_dihapus_tidak_lagi_memberi_permission()
    {
        var client = Admin();
        var user = await CreateUserAsync(client);
        var code = NewCode();
        var role = await CreateRoleAsync(client, ["task.read"], code);
        await AssignAsync(client, user.Id, new { roleId = role.Id, scopeType = "global" });

        // Simulasi hapus yang berlomba dengan penugasan: baris USER_ROLES tertinggal menunjuk role yang dihapus.
        await fixture.ScalarAsync($"UPDATE ROLES SET IS_DELETED = 1 WHERE TENANT_ID = {OracleUserFixture.TenantA} AND CODE = '{code}'");

        var body = await client.GetFromJsonAsync<UserPermissionsBody>($"/api/v1/users/{user.Id}/permissions");
        body!.Roles.Should().BeEmpty();
        body.Permissions.Should().BeEmpty();
    }

    [Fact]
    public async Task Seeder_menonaktifkan_permission_yang_hilang_dari_katalog_dan_memulihkan_deskripsi()
    {
        await fixture.ScalarAsync("INSERT INTO PERMISSIONS (CODE, MODULE, DESCRIPTION, CREATED_AT, CREATED_BY) VALUES ('usang.hapus', 'usang', 'dihapus dari katalog', SYSTIMESTAMP, 0)");
        await fixture.ScalarAsync("UPDATE PERMISSIONS SET DESCRIPTION = 'diubah manual' WHERE CODE = 'task.read'");
        var client = Admin();
        (await client.GetFromJsonAsync<List<PermissionBody>>("/api/v1/permissions"))!.Should().Contain(p => p.Code == "usang.hapus");

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<DefaultRoleProvisioner>().ProvisionAllAsync(default);

        var catalog = await client.GetFromJsonAsync<List<PermissionBody>>("/api/v1/permissions");
        catalog!.Should().NotContain(p => p.Code == "usang.hapus");
        catalog.Single(p => p.Code == "task.read").Description.Should().Be(DefaultRoleCatalog.Permissions.Single(p => p.Code == "task.read").Description);
        (await client.PostAsJsonAsync("/api/v1/roles", new { code = NewCode(), name = "X", permissions = new[] { "usang.hapus" } }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Role_kustom_lama_berkode_bawaan_tidak_menggagalkan_seed_tenant_lain()
    {
        const long conflictTenant = 3;
        await fixture.ScalarAsync($"INSERT INTO TENANTS (ID, CODE, NAME, CREATED_AT, CREATED_BY) VALUES ({conflictTenant}, 'tenant-c', 'Tenant C', SYSTIMESTAMP, 0)");
        await fixture.ScalarAsync(
            $"INSERT INTO ROLES (TENANT_ID, CODE, NAME, IS_SYSTEM, CREATED_AT, CREATED_BY) VALUES ({conflictTenant}, 'AUDITOR', 'Kustom Lama', 0, SYSTIMESTAMP, 0)");

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var provisioner = scope.ServiceProvider.GetRequiredService<DefaultRoleProvisioner>();
            await FluentActions.Awaiting(() => provisioner.ProvisionAllAsync(default)).Should().NotThrowAsync();
        }

        // Role kustom tidak ditimpa; role bawaan lain tetap dibuat; tenant lain tidak terpengaruh.
        Convert.ToInt32(await fixture.ScalarAsync($"SELECT COUNT(*) FROM ROLES WHERE TENANT_ID = {conflictTenant} AND IS_SYSTEM = 1"))
            .Should().Be(DefaultRoleCatalog.Roles.Count - 1);
        Convert.ToInt32(await fixture.ScalarAsync($"SELECT IS_SYSTEM FROM ROLES WHERE TENANT_ID = {conflictTenant} AND CODE = 'AUDITOR'")).Should().Be(0);
        Convert.ToInt32(await fixture.ScalarAsync($"SELECT COUNT(*) FROM ROLES WHERE TENANT_ID = {OracleUserFixture.TenantA} AND IS_SYSTEM = 1"))
            .Should().Be(DefaultRoleCatalog.Roles.Count);
    }
}
