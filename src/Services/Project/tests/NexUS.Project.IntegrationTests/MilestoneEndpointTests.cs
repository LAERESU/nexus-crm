using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Oracle.ManagedDataAccess.Client;

namespace NexUS.Project.IntegrationTests;

[Collection(OracleCollection.Name)]
[Trait("Category", "Oracle")]
public class MilestoneEndpointTests(OracleProjectFixture fixture)
{
    private const long A = OracleProjectFixture.TenantA;

    private sealed record ProjectBody(Guid Id, string Code, long RowVersion);

    private sealed record MilestoneBody(
        Guid Id, Guid ProjectId, string Name, string? Description, DateOnly DueDate, string Status, long RowVersion);

    private sealed record MilestonePage(List<MilestoneBody> Items, int Page, int Size, long TotalCount);

    private async Task<ProjectBody> CreateProjectAsync(HttpClient client, string? start = "2026-12-01", string? end = "2026-12-31")
    {
        var response = await client.PostAsJsonAsync("/api/v1/projects",
            new { code = $"MS{Guid.NewGuid():N}"[..12], name = "Proyek Milestone", startDate = start, endDate = end });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ProjectBody>())!;
    }

    private static string Url(Guid projectId, Guid? milestoneId = null) =>
        $"/api/v1/projects/{projectId}/milestones" + (milestoneId is null ? string.Empty : $"/{milestoneId}");

    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, Guid projectId, string dueDate, string name = "Rilis") =>
        client.PostAsJsonAsync(Url(projectId), new { name, description = "Catatan", dueDate });

    private static HttpRequestMessage Put(string url, long rowVersion, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{rowVersion}\"");
        return request;
    }

    [Fact]
    public async Task Milestone_di_luar_rentang_tanggal_proyek_ditolak_422()
    {
        var manager = fixture.ClientAs(A);
        var project = await CreateProjectAsync(manager);

        foreach (var due in new[] { "2026-11-30", "2027-01-01" })
        {
            var response = await CreateAsync(manager, project.Id, due);
            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString()
                .Should().Contain("2026-12-01 s.d. 2026-12-31");
        }

        (await CreateAsync(manager, project.Id, "2026-12-01")).StatusCode.Should().Be(HttpStatusCode.Created, "batas awal termasuk");
        (await CreateAsync(manager, project.Id, "2026-12-31")).StatusCode.Should().Be(HttpStatusCode.Created, "batas akhir termasuk");

        // Proyek tanpa rentang tanggal tidak membatasi milestone.
        var open = await CreateProjectAsync(manager, null, null);
        (await CreateAsync(manager, open.Id, "2030-06-01")).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Membuat_mengubah_dan_mendaftar_milestone()
    {
        var manager = fixture.ClientAs(A);
        var project = await CreateProjectAsync(manager);

        var created = await CreateAsync(manager, project.Id, "2026-12-20", "Rilis Akhir");
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        created.Headers.ETag!.Tag.Should().Be("\"0\"");
        var milestone = (await created.Content.ReadFromJsonAsync<MilestoneBody>())!;
        milestone.Should().Match<MilestoneBody>(m =>
            m.ProjectId == project.Id && m.Status == "open" && m.DueDate == new DateOnly(2026, 12, 20) && m.Description == "Catatan");
        (await CreateAsync(manager, project.Id, "2026-12-05", "Rilis Beta")).StatusCode.Should().Be(HttpStatusCode.Created);

        var page = await manager.GetFromJsonAsync<MilestonePage>(Url(project.Id));
        page!.TotalCount.Should().Be(2);
        page.Items.Select(m => m.Name).Should().Equal("Rilis Beta", "Rilis Akhir"); // urut tanggal jatuh tempo

        var updated = await manager.SendAsync(Put(Url(project.Id, milestone.Id), milestone.RowVersion,
            new { name = "Rilis 1.0", description = (string?)null, dueDate = "2026-12-31", status = "completed" }));
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await updated.Content.ReadFromJsonAsync<MilestoneBody>())!;
        body.Should().Match<MilestoneBody>(m => m.Name == "Rilis 1.0" && m.Status == "completed" && m.Description == null);
        body.RowVersion.Should().Be(milestone.RowVersion + 1);

        // Versi usang, tanggal di luar rentang, status tidak dikenal, tanpa If-Match, milestone tidak dikenal.
        var url = Url(project.Id, milestone.Id);
        (await manager.SendAsync(Put(url, milestone.RowVersion, new { name = "X", dueDate = "2026-12-10", status = "open" })))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await manager.SendAsync(Put(url, body.RowVersion, new { name = "X", dueDate = "2027-01-10", status = "open" })))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await manager.SendAsync(Put(url, body.RowVersion, new { name = "X", dueDate = "2026-12-10", status = "selesai" })))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await manager.PutAsJsonAsync(url, new { name = "X", dueDate = "2026-12-10", status = "open" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await manager.SendAsync(Put(Url(project.Id, Guid.NewGuid()), 0, new { name = "X", dueDate = "2026-12-10", status = "open" })))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Milestone proyek lain tidak bisa diubah lewat URL proyek ini.
        var other = await CreateProjectAsync(manager);
        (await manager.SendAsync(Put(Url(other.Id, milestone.Id), body.RowVersion, new { name = "X", dueDate = "2026-12-10", status = "open" })))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        Convert.ToInt32(await fixture.ScalarAsync(
            "SELECT COUNT(*) FROM OUTBOX_MESSAGES WHERE TYPE LIKE :type AND PAYLOAD LIKE :payload",
            new OracleParameter("type", "%Milestone%DomainEvent%"),
            new OracleParameter("payload", $"%{milestone.Id}%"))).Should().Be(2, "dibuat lalu diubah sekali");
    }

    [Fact]
    public async Task Rentang_tanggal_proyek_tidak_bisa_diubah_sampai_mengecualikan_milestone()
    {
        var manager = fixture.ClientAs(A);
        var project = await CreateProjectAsync(manager);
        (await CreateAsync(manager, project.Id, "2026-12-20")).StatusCode.Should().Be(HttpStatusCode.Created);
        var url = $"/api/v1/projects/{project.Id}";
        var current = (await manager.GetFromJsonAsync<ProjectBody>(url))!;

        object Body(string start, string end) => new { name = "Proyek Milestone", status = "active", priority = "medium", startDate = start, endDate = end };

        (await manager.SendAsync(Put(url, current.RowVersion, Body("2026-12-01", "2026-12-15")))).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity);
        (await manager.SendAsync(Put(url, current.RowVersion, Body("2026-12-21", "2026-12-31")))).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity);
        (await manager.SendAsync(Put(url, current.RowVersion, Body("2026-12-20", "2026-12-20")))).StatusCode
            .Should().Be(HttpStatusCode.OK, "rentang masih mencakup milestone");
    }

    [Fact]
    public async Task Akses_milestone_mengikuti_keanggotaan_dan_peran_proyek()
    {
        var manager = fixture.ClientAs(A);
        var project = await CreateProjectAsync(manager);
        (await CreateAsync(manager, project.Id, "2026-12-10")).StatusCode.Should().Be(HttpStatusCode.Created);

        // Bukan anggota: tidak bisa membaca maupun membuat.
        var outsiderId = Guid.NewGuid();
        fixture.Grant(A, outsiderId, "project.read", "project.update");
        var outsider = fixture.ClientAs(A, outsiderId);
        (await outsider.GetAsync(Url(project.Id))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CreateAsync(outsider, project.Id, "2026-12-11")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Anggota berperan member: boleh membaca, tidak boleh membuat walau memegang project.update.
        (await manager.PostAsJsonAsync($"/api/v1/projects/{project.Id}/members", new { userId = outsiderId, role = "member" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await outsider.GetFromJsonAsync<MilestonePage>(Url(project.Id)))!.Items.Should().ContainSingle();
        (await CreateAsync(outsider, project.Id, "2026-12-11")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Tanpa project.update sama sekali (MEMBER bawaan): ditolak di policy.
        (await CreateAsync(fixture.MemberClient(A), project.Id, "2026-12-11")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Admin tenant membaca tanpa menjadi anggota; proyek tenant lain tidak ada.
        (await fixture.AdminClient(A).GetAsync(Url(project.Id))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await fixture.AdminClient(OracleProjectFixture.TenantB).GetAsync(Url(project.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Milestone_proyek_yang_diarsipkan_tidak_bisa_dibuat()
    {
        var manager = fixture.ClientAs(A);
        var project = await CreateProjectAsync(manager);
        var archive = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/projects/{project.Id}");
        archive.Headers.TryAddWithoutValidation("If-Match", $"\"{project.RowVersion}\"");
        (await manager.SendAsync(archive)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await CreateAsync(manager, project.Id, "2026-12-10")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
