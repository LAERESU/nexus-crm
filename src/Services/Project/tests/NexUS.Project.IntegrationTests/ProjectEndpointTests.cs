using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Oracle.ManagedDataAccess.Client;

namespace NexUS.Project.IntegrationTests;

[Collection(OracleCollection.Name)]
[Trait("Category", "Oracle")]
public class ProjectEndpointTests(OracleProjectFixture fixture)
{
    private const long A = OracleProjectFixture.TenantA;
    private const long B = OracleProjectFixture.TenantB;

    private sealed record ProjectBody(
        Guid Id, string Code, string Name, string? Description, string Status, DateOnly? StartDate, DateOnly? EndDate,
        Guid OwnerId, Guid? DepartmentId, long RowVersion);

    private sealed record Paged(List<ProjectBody> Items, int Page, int Size, long TotalCount);

    private static string NewCode() => $"P{Guid.NewGuid():N}"[..12];

    private static object NewProject(string? code = null, string name = "Proyek Uji", Guid? departmentId = null) => new
    {
        code = code ?? NewCode(),
        name,
        description = "Deskripsi",
        startDate = "2026-11-16",
        endDate = "2026-12-31",
        departmentId,
    };

    private async Task<ProjectBody> CreateAsync(HttpClient client, string? code = null, string name = "Proyek Uji", Guid? departmentId = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/projects", NewProject(code, name, departmentId));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ProjectBody>())!;
    }

    private static HttpRequestMessage WithIfMatch(HttpMethod method, string url, long rowVersion, object? body = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{rowVersion}\"");
        return request;
    }

    private static object UpdateBody(string name = "Proyek Diubah", string status = "active") => new
    {
        name,
        description = (string?)null,
        status,
        startDate = "2026-11-16",
        endDate = "2027-01-31",
        departmentId = (Guid?)null,
    };

    private static async Task<List<ProjectBody>> ListAsync(HttpClient client, string query)
    {
        var page = await client.GetFromJsonAsync<Paged>($"/api/v1/projects?size=100&{query}");
        return page!.Items;
    }

    [Fact]
    public async Task Membuat_proyek_menyimpan_data_dan_menjadikan_pembuat_manager()
    {
        var client = fixture.ClientAs(A);
        var code = NewCode();

        var response = await client.PostAsJsonAsync("/api/v1/projects", NewProject(code.ToLowerInvariant()));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var project = (await response.Content.ReadFromJsonAsync<ProjectBody>())!;
        response.Headers.Location!.ToString().Should().EndWith($"/api/v1/projects/{project.Id}");
        response.Headers.ETag!.Tag.Should().Be("\"0\"");
        project.Code.Should().Be(code.ToUpperInvariant());
        project.Status.Should().Be("planning");
        project.StartDate.Should().Be(new DateOnly(2026, 11, 16));
        project.EndDate.Should().Be(new DateOnly(2026, 12, 31));
        project.OwnerId.Should().Be(OracleProjectFixture.ManagerId(A));

        (await client.GetFromJsonAsync<ProjectBody>($"/api/v1/projects/{project.Id}")).Should().Be(project);

        var role = await fixture.ScalarAsync(
            "SELECT m.ROLE FROM PROJECT_MEMBERS m JOIN PROJECTS p ON p.ID = m.PROJECT_ID WHERE p.PUBLIC_ID = :id AND m.USER_ID = :usr",
            new OracleParameter("id", OracleDbType.Raw) { Value = project.Id.ToByteArray() },
            new OracleParameter("usr", OracleDbType.Raw) { Value = OracleProjectFixture.ManagerId(A).ToByteArray() });
        role.Should().Be("MANAGER");

        var events = Convert.ToInt32(await fixture.ScalarAsync(
            $"SELECT COUNT(*) FROM OUTBOX_MESSAGES WHERE TYPE LIKE '%ProjectCreatedDomainEvent%' AND PAYLOAD LIKE '%{code.ToUpperInvariant()}%'"));
        events.Should().Be(1);
    }

    [Fact]
    public async Task Kode_ganda_ditolak_per_tenant_dan_validasi_mengembalikan_error_per_field()
    {
        var client = fixture.ClientAs(A);
        var existing = await CreateAsync(client);

        var duplicate = await client.PostAsJsonAsync("/api/v1/projects", NewProject(existing.Code));
        duplicate.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // Tenant lain boleh memakai kode yang sama.
        (await fixture.ClientAs(B).PostAsJsonAsync("/api/v1/projects", NewProject(existing.Code))).StatusCode.Should().Be(HttpStatusCode.Created);

        var invalid = await client.PostAsJsonAsync("/api/v1/projects", new { code = "1 buruk", name = "", startDate = "2026-12-31", endDate = "2026-01-01" });
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errors = (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        errors.EnumerateObject().Select(p => p.Name).Should().Contain(["Code", "Name", "EndDate"]);
    }

    [Fact]
    public async Task Update_dengan_If_Match_berhasil_dan_row_version_usang_ditolak_409()
    {
        var client = fixture.ClientAs(A);
        var project = await CreateAsync(client);
        var url = $"/api/v1/projects/{project.Id}";

        var updated = await client.SendAsync(WithIfMatch(HttpMethod.Put, url, project.RowVersion, UpdateBody()));
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await updated.Content.ReadFromJsonAsync<ProjectBody>())!;
        body.Name.Should().Be("Proyek Diubah");
        body.Status.Should().Be("active");
        body.Description.Should().BeNull();
        body.EndDate.Should().Be(new DateOnly(2027, 1, 31));
        body.RowVersion.Should().Be(project.RowVersion + 1);
        updated.Headers.ETag!.Tag.Should().Be($"\"{body.RowVersion}\"");

        var stale = await client.SendAsync(WithIfMatch(HttpMethod.Put, url, project.RowVersion, UpdateBody("Basi")));
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.GetFromJsonAsync<ProjectBody>(url))!.Name.Should().Be("Proyek Diubah");

        var missing = await client.PutAsJsonAsync(url, UpdateBody());
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest, "If-Match wajib");

        var archivedByUpdate = await client.SendAsync(WithIfMatch(HttpMethod.Put, url, body.RowVersion, UpdateBody(status: "archived")));
        archivedByUpdate.StatusCode.Should().Be(HttpStatusCode.BadRequest, "arsip hanya lewat DELETE");
    }

    [Fact]
    public async Task Proyek_yang_diarsipkan_tidak_muncul_di_list_default_tetapi_ada_di_filter_archived()
    {
        var client = fixture.ClientAs(A);
        var marker = $"arsip-{Guid.NewGuid():N}";
        var kept = await CreateAsync(client, name: $"{marker} tetap");
        var archived = await CreateAsync(client, name: $"{marker} diarsipkan");
        var url = $"/api/v1/projects/{archived.Id}";

        (await client.SendAsync(WithIfMatch(HttpMethod.Delete, url, archived.RowVersion + 5))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.SendAsync(WithIfMatch(HttpMethod.Delete, url, archived.RowVersion))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ListAsync(client, $"search={marker}")).Select(p => p.Id).Should().BeEquivalentTo([kept.Id]);
        (await ListAsync(client, $"search={marker}&status=archived")).Select(p => p.Id).Should().BeEquivalentTo([archived.Id]);

        // Tetap bisa dibuka lewat ID, tetapi tidak bisa diubah lagi.
        var detail = (await client.GetFromJsonAsync<ProjectBody>(url))!;
        detail.Status.Should().Be("archived");
        (await client.SendAsync(WithIfMatch(HttpMethod.Put, url, detail.RowVersion, UpdateBody()))).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task List_mendukung_filter_status_pemilik_departemen_pencarian_dan_paging()
    {
        var manager = fixture.ClientAs(A);
        var otherOwner = Guid.NewGuid();
        fixture.Grant(A, otherOwner, "project.read", "project.create", "project.update");
        var marker = $"filter-{Guid.NewGuid():N}";
        var department = Guid.NewGuid();

        var first = await CreateAsync(manager, name: $"{marker} A", departmentId: department);
        var second = await CreateAsync(manager, name: $"{marker} B");
        var third = await CreateAsync(fixture.ClientAs(A, otherOwner), name: $"{marker} C");
        (await manager.SendAsync(WithIfMatch(HttpMethod.Put, $"/api/v1/projects/{second.Id}", second.RowVersion, UpdateBody($"{marker} B", "on_hold"))))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await ListAsync(manager, $"search={marker.ToUpperInvariant()}")).Select(p => p.Id).Should().BeEquivalentTo([first.Id, second.Id, third.Id]);
        (await ListAsync(manager, $"search={first.Code.ToLowerInvariant()}")).Select(p => p.Id).Should().BeEquivalentTo([first.Id]);
        (await ListAsync(manager, $"search={marker}&status=on_hold")).Select(p => p.Id).Should().BeEquivalentTo([second.Id]);
        (await ListAsync(manager, $"search={marker}&ownerId={otherOwner}")).Select(p => p.Id).Should().BeEquivalentTo([third.Id]);
        (await ListAsync(manager, $"search={marker}&departmentId={department}")).Select(p => p.Id).Should().BeEquivalentTo([first.Id]);

        var page = await manager.GetFromJsonAsync<Paged>($"/api/v1/projects?search={marker}&page=2&size=2");
        page!.TotalCount.Should().Be(3);
        page.Size.Should().Be(2);
        page.Items.Should().ContainSingle().Which.Id.Should().Be(third.Id, "urut nama");

        (await manager.GetAsync("/api/v1/projects?status=tidak-ada")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await manager.GetAsync("/api/v1/projects?size=101")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Data_tenant_lain_tidak_terlihat_dan_tidak_bisa_diubah()
    {
        var project = await CreateAsync(fixture.ClientAs(A));
        var other = fixture.ClientAs(B);
        var url = $"/api/v1/projects/{project.Id}";

        (await other.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.SendAsync(WithIfMatch(HttpMethod.Put, url, project.RowVersion, UpdateBody()))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.SendAsync(WithIfMatch(HttpMethod.Delete, url, project.RowVersion))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ListAsync(other, $"search={project.Code}")).Should().BeEmpty();
    }

    [Fact]
    public async Task Endpoint_dijaga_permission()
    {
        var project = await CreateAsync(fixture.ClientAs(A));
        var url = $"/api/v1/projects/{project.Id}";

        // Tanpa login.
        (await fixture.Factory.CreateClient().GetAsync("/api/v1/projects")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Anggota (hanya project.read): boleh membaca, tidak boleh membuat, mengubah, atau mengarsipkan.
        var member = fixture.MemberClient(A);
        (await member.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await member.GetAsync("/api/v1/projects")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await member.PostAsJsonAsync("/api/v1/projects", NewProject())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.SendAsync(WithIfMatch(HttpMethod.Put, url, project.RowVersion, UpdateBody()))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.SendAsync(WithIfMatch(HttpMethod.Delete, url, project.RowVersion))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // User tanpa permission sama sekali.
        (await fixture.ClientAs(A, Guid.NewGuid()).GetAsync("/api/v1/projects")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
