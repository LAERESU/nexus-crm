using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace NexUS.User.IntegrationTests;

[Collection(OracleCollection.Name)]
[Trait("Category", "Oracle")]
public class DepartmentEndpointTests(OracleUserFixture fixture)
{
    private sealed record DepartmentBody(Guid Id, string Code, string Name, Guid? ParentId, long RowVersion);

    private static string NewCode() => $"D{Guid.NewGuid():N}"[..12];

    private static async Task<DepartmentBody> CreateAsync(HttpClient client, string? code = null, Guid? parentId = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/departments", new { code = code ?? NewCode(), name = "Departemen", parentId });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<DepartmentBody>())!;
    }

    private static HttpRequestMessage WithVersion(HttpMethod method, string url, long version, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        return request;
    }

    [Fact]
    public async Task Kode_unik_per_tenant_dan_dinormalisasi_ke_huruf_besar()
    {
        var client = fixture.ClientAs(OracleUserFixture.TenantA);
        var code = NewCode();
        var created = await CreateAsync(client, code.ToLowerInvariant());
        created.Code.Should().Be(code.ToUpperInvariant());

        var duplicate = await client.PostAsJsonAsync("/api/v1/departments", new { code, name = "Lain" });

        duplicate.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await fixture.ClientAs(OracleUserFixture.TenantB).PostAsJsonAsync("/api/v1/departments", new { code, name = "Lain" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Hierarki_menolak_siklus()
    {
        var client = fixture.ClientAs(OracleUserFixture.TenantA);
        var parent = await CreateAsync(client);
        var child = await CreateAsync(client, parentId: parent.Id);

        // Menjadikan induk sebagai anak dari turunannya sendiri membentuk siklus.
        var response = await client.SendAsync(WithVersion(HttpMethod.Put, $"/api/v1/departments/{parent.Id}", parent.RowVersion,
            new { name = "Induk", parentId = child.Id }));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Departemen_tenant_lain_tidak_dapat_dijadikan_induk()
    {
        var parentA = await CreateAsync(fixture.ClientAs(OracleUserFixture.TenantA));

        var response = await fixture.ClientAs(OracleUserFixture.TenantB)
            .PostAsJsonAsync("/api/v1/departments", new { code = NewCode(), name = "X", parentId = parentA.Id });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Hapus_ditolak_bila_masih_punya_sub_departemen_atau_user()
    {
        var client = fixture.ClientAs(OracleUserFixture.TenantA);
        var parent = await CreateAsync(client);
        await CreateAsync(client, parentId: parent.Id);

        var withChild = await client.SendAsync(WithVersion(HttpMethod.Delete, $"/api/v1/departments/{parent.Id}", parent.RowVersion));
        withChild.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var empty = await CreateAsync(client);
        await client.PostAsJsonAsync("/api/v1/users", new { email = $"d-{Guid.NewGuid():N}@contoh.com", fullName = "Anggota", departmentId = empty.Id });
        var withUser = await client.SendAsync(WithVersion(HttpMethod.Delete, $"/api/v1/departments/{empty.Id}", empty.RowVersion));
        withUser.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Hapus_departemen_kosong_adalah_soft_delete()
    {
        var client = fixture.ClientAs(OracleUserFixture.TenantA);
        var department = await CreateAsync(client);

        var response = await client.SendAsync(WithVersion(HttpMethod.Delete, $"/api/v1/departments/{department.Id}", department.RowVersion));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/v1/departments/{department.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var flag = await fixture.ScalarAsync($"SELECT IS_DELETED FROM DEPARTMENTS WHERE CODE = '{department.Code}' AND TENANT_ID = {OracleUserFixture.TenantA}");
        Convert.ToInt32(flag).Should().Be(1);
    }
}
