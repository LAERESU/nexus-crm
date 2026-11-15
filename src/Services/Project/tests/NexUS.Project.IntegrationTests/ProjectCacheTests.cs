using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Oracle.ManagedDataAccess.Client;

namespace NexUS.Project.IntegrationTests;

/// <summary>Cache-aside detail dan keanggotaan proyek di Redis sungguhan (NX-029).</summary>
[Collection(OracleCollection.Name)]
[Trait("Category", "Oracle")]
public class ProjectCacheTests(OracleProjectFixture fixture)
{
    private const long A = OracleProjectFixture.TenantA;

    private sealed record ProjectBody(Guid Id, string Code, string Name, long RowVersion);

    private async Task<ProjectBody> CreateAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/projects",
            new { code = $"C{Guid.NewGuid():N}"[..12], name = "Proyek Cache", startDate = "2026-12-01", endDate = "2026-12-31" });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ProjectBody>())!;
    }

    private static string ProjectKey(Guid id) => $"tnt:{A}:project:{id:D}";

    private static string MembersKey(Guid id) => $"tnt:{A}:project:{id:D}:members";

    /// <summary>Mengubah nama langsung di database, melewati aplikasi (cache tidak tahu).</summary>
    private Task RenameBehindTheAppAsync(Guid id, string name) => fixture.ScalarAsync(
        "UPDATE PROJECTS SET NAME = :name WHERE PUBLIC_ID = :id",
        new OracleParameter("name", name), new OracleParameter("id", OracleDbType.Raw) { Value = id.ToByteArray() });

    private static HttpRequestMessage Put(string url, long rowVersion, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{rowVersion}\"");
        return request;
    }

    [Fact]
    public async Task Request_detail_kedua_dilayani_dari_cache()
    {
        var client = fixture.ClientAs(A);
        var project = await CreateAsync(client);
        var url = $"/api/v1/projects/{project.Id}";
        var db = fixture.Redis.GetDatabase();

        (await client.GetFromJsonAsync<ProjectBody>(url))!.Name.Should().Be("Proyek Cache");
        (await db.KeyExistsAsync(ProjectKey(project.Id))).Should().BeTrue("detail pertama mengisi cache");
        (await db.KeyTimeToLiveAsync(ProjectKey(project.Id))).Should().BeGreaterThan(TimeSpan.Zero).And.BeLessThanOrEqualTo(TimeSpan.FromMinutes(10));
        (await db.KeyExistsAsync(MembersKey(project.Id))).Should().BeTrue("pemeriksaan akses mengisi cache keanggotaan");

        // Database diubah di luar aplikasi: request kedua tetap menjawab dari cache, bukan dari database.
        await RenameBehindTheAppAsync(project.Id, "Diubah Langsung");
        (await client.GetFromJsonAsync<ProjectBody>(url))!.Name.Should().Be("Proyek Cache");
    }

    [Fact]
    public async Task Perubahan_proyek_lewat_aplikasi_langsung_terlihat()
    {
        var client = fixture.ClientAs(A);
        var project = await CreateAsync(client);
        var url = $"/api/v1/projects/{project.Id}";
        var cached = (await client.GetFromJsonAsync<ProjectBody>(url))!;

        var update = await client.SendAsync(Put(url, cached.RowVersion, new { name = "Nama Baru", status = "active", priority = "high" }));
        update.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = (await client.GetFromJsonAsync<ProjectBody>(url))!;
        after.Name.Should().Be("Nama Baru");
        after.RowVersion.Should().Be(cached.RowVersion + 1);

        // Milestone baru menaikkan versi proyek: detail tidak boleh menyajikan versi lama (If-Match berikutnya akan 409).
        (await client.PostAsJsonAsync($"{url}/milestones", new { name = "M", dueDate = "2026-12-10" })).StatusCode
            .Should().Be(HttpStatusCode.Created);
        (await client.GetFromJsonAsync<ProjectBody>(url))!.RowVersion.Should().Be(after.RowVersion + 1);

        // Arsip juga menghapus cache.
        var archive = new HttpRequestMessage(HttpMethod.Delete, url);
        archive.Headers.TryAddWithoutValidation("If-Match", $"\"{after.RowVersion + 1}\"");
        (await client.SendAsync(archive)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<System.Text.Json.JsonElement>(url)).GetProperty("status").GetString().Should().Be("archived");
    }

    [Fact]
    public async Task Perubahan_anggota_langsung_berlaku_untuk_pemeriksaan_akses()
    {
        var manager = fixture.ClientAs(A);
        var project = await CreateAsync(manager);
        var userId = Guid.NewGuid();
        fixture.Grant(A, userId, "project.read");
        var user = fixture.ClientAs(A, userId);
        var url = $"/api/v1/projects/{project.Id}";

        (await user.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden); // keanggotaan kini ter-cache tanpa user ini

        (await manager.PostAsJsonAsync($"{url}/members", new { userId, role = "member" })).StatusCode.Should().Be(HttpStatusCode.Created);
        (await user.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK, "cache keanggotaan dihapus saat anggota ditambah");

        (await manager.DeleteAsync($"{url}/members/{userId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await user.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "cache keanggotaan dihapus saat anggota dikeluarkan");
    }

    [Fact]
    public async Task Pembacaan_lama_tidak_bisa_menulis_ulang_cache_setelah_invalidasi()
    {
        var cache = fixture.Factory.Services.GetRequiredService<NexUS.Project.Infrastructure.Caching.ProjectCache>();
        var key = ProjectKey(Guid.NewGuid());

        // Urutan race: perubahan meng-invalidasi kunci, lalu pembacaan lambat (data lama) mencoba mengisi cache.
        await cache.InvalidateAsync([key]);
        await cache.TrySetAsync(key, new { name = "lama" }, TimeSpan.FromMinutes(10));

        (await cache.GetAsync<System.Text.Json.Nodes.JsonObject>(key)).Should().BeNull("tombstone menolak pengisian data lama");
        (await fixture.Redis.GetDatabase().KeyTimeToLiveAsync(key)).Should().BeLessThanOrEqualTo(
            NexUS.Project.Infrastructure.Caching.ProjectCache.TombstoneTtl);
    }

    [Fact]
    public async Task Isi_cache_yang_tidak_bisa_dibaca_dianggap_miss()
    {
        var client = fixture.ClientAs(A);
        var project = await CreateAsync(client);
        var db = fixture.Redis.GetDatabase();
        await db.StringSetAsync(ProjectKey(project.Id), "{rusak");
        await db.StringSetAsync(MembersKey(project.Id), "[{\"userId\":123}]");

        var response = await client.GetAsync($"/api/v1/projects/{project.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK, "cache rusak tidak boleh membuat request gagal");
        (await response.Content.ReadFromJsonAsync<ProjectBody>())!.Name.Should().Be("Proyek Cache");
    }

    [Fact]
    public async Task Proyek_yang_tidak_ada_tidak_di_cache()
    {
        var missing = Guid.NewGuid();

        (await fixture.AdminClient(A).GetAsync($"/api/v1/projects/{missing}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await fixture.ClientAs(A).GetAsync($"/api/v1/projects/{missing}/members")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var db = fixture.Redis.GetDatabase();
        (await db.KeyExistsAsync(ProjectKey(missing))).Should().BeFalse();
        (await db.KeyExistsAsync(MembersKey(missing))).Should().BeFalse();
    }
}
