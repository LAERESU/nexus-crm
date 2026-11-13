using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexUS.BuildingBlocks.Messaging;
using NexUS.BuildingBlocks.Messaging.Contracts;
using Oracle.ManagedDataAccess.Client;

namespace NexUS.Project.IntegrationTests;

[Collection(OracleCollection.Name)]
[Trait("Category", "Oracle")]
public class ProjectMemberEndpointTests(OracleProjectFixture fixture)
{
    private const long A = OracleProjectFixture.TenantA;
    private const long B = OracleProjectFixture.TenantB;

    private sealed record ProjectBody(Guid Id, string Code, string Status, long RowVersion);

    private sealed record Paged(List<ProjectBody> Items, long TotalCount);

    private sealed record MemberBody(Guid UserId, string? FullName, string? Email, string Role, DateTimeOffset JoinedAt, bool? IsActive);

    private async Task<ProjectBody> CreateProjectAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/projects", new { code = $"M{Guid.NewGuid():N}"[..12], name = "Proyek Anggota" });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ProjectBody>())!;
    }

    private static string Members(Guid projectId, Guid? userId = null) =>
        $"/api/v1/projects/{projectId}/members" + (userId is null ? string.Empty : $"/{userId}");

    private static Task<HttpResponseMessage> AddAsync(HttpClient client, Guid projectId, Guid userId, string role = "member") =>
        client.PostAsJsonAsync(Members(projectId), new { userId, role });

    private static async Task<List<MemberBody>> ListAsync(HttpClient client, Guid projectId) =>
        (await client.GetFromJsonAsync<List<MemberBody>>(Members(projectId)))!;

    /// <summary>User baru dengan permission global tertentu (bawaan: hanya membaca proyek).</summary>
    private Guid NewUser(params string[] permissions)
    {
        var id = Guid.NewGuid();
        fixture.Grant(A, id, permissions.Length == 0 ? ["project.read"] : permissions);
        return id;
    }

    /// <summary>Publish bersifat mandatory: diulang sampai antrean consumer Project Service siap menerima.</summary>
    private async Task PublishAsync(IIntegrationEvent integrationEvent)
    {
        var publisher = fixture.Factory.Services.GetRequiredService<IEventPublisher>();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await publisher.PublishAsync(integrationEvent);
                return;
            }
            catch (Exception) when (attempt < 60)
            {
                await Task.Delay(500);
            }
        }
    }

    private async Task WaitForUserRefAsync(Guid userId, Func<MemberBody, bool> condition, HttpClient client, Guid projectId)
    {
        for (var attempt = 0; attempt < 120; attempt++)
        {
            if ((await ListAsync(client, projectId)).FirstOrDefault(m => m.UserId == userId) is { } member && condition(member)) return;
            await Task.Delay(250);
        }

        throw new TimeoutException($"Read model user {userId} tidak mencapai keadaan yang diharapkan.");
    }

    [Fact]
    public async Task Bukan_anggota_ditolak_403_saat_membuka_proyek_dan_tidak_melihatnya_di_daftar()
    {
        var manager = fixture.ClientAs(A);
        var project = await CreateProjectAsync(manager);
        var outsiderId = NewUser();
        var outsider = fixture.ClientAs(A, outsiderId);

        (await outsider.GetAsync($"/api/v1/projects/{project.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await outsider.GetAsync(Members(project.Id))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await outsider.GetFromJsonAsync<Paged>($"/api/v1/projects?search={project.Code}"))!.Items.Should().BeEmpty();

        // Setelah dijadikan anggota (peran apa pun), proyek terbuka dan muncul di daftarnya.
        (await AddAsync(manager, project.Id, outsiderId, "viewer")).StatusCode.Should().Be(HttpStatusCode.Created);
        (await outsider.GetAsync($"/api/v1/projects/{project.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await outsider.GetFromJsonAsync<Paged>($"/api/v1/projects?search={project.Code}"))!.Items.Should().ContainSingle();
        (await ListAsync(outsider, project.Id)).Select(m => m.UserId).Should().Contain(outsiderId);

        // Admin tenant dan auditor (project.all.read) membuka proyek tanpa menjadi anggota.
        (await fixture.AdminClient(A).GetAsync($"/api/v1/projects/{project.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await fixture.AdminClient(A).GetFromJsonAsync<Paged>($"/api/v1/projects?search={project.Code}"))!.Items.Should().ContainSingle();

        // Proyek tenant lain tetap tidak ada, juga bagi adminnya.
        (await fixture.AdminClient(B).GetAsync(Members(project.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Manager_menambah_mengubah_peran_dan_mengeluarkan_anggota()
    {
        var manager = fixture.ClientAs(A);
        var project = await CreateProjectAsync(manager);
        var userId = NewUser();

        var added = await AddAsync(manager, project.Id, userId);
        added.StatusCode.Should().Be(HttpStatusCode.Created);
        (await added.Content.ReadFromJsonAsync<MemberBody>())!.Should().Match<MemberBody>(m => m.UserId == userId && m.Role == "member");

        (await AddAsync(manager, project.Id, userId)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "sudah anggota");
        (await AddAsync(manager, project.Id, NewUser(), "pemilik")).StatusCode.Should().Be(HttpStatusCode.BadRequest, "peran tidak dikenal");
        (await AddAsync(manager, project.Id, Guid.Empty)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var changed = await manager.PutAsJsonAsync(Members(project.Id, userId), new { role = "viewer" });
        changed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await changed.Content.ReadFromJsonAsync<MemberBody>())!.Role.Should().Be("viewer");
        (await manager.PutAsJsonAsync(Members(project.Id, Guid.NewGuid()), new { role = "viewer" })).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await ListAsync(manager, project.Id)).Select(m => (m.UserId, m.Role)).Should().BeEquivalentTo(
            [(OracleProjectFixture.ManagerId(A), "manager"), (userId, "viewer")], options => options.WithStrictOrdering());

        (await manager.DeleteAsync(Members(project.Id, userId))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await manager.DeleteAsync(Members(project.Id, userId))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ListAsync(manager, project.Id)).Should().ContainSingle();
        (await fixture.ClientAs(A, userId).GetAsync($"/api/v1/projects/{project.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Anggota yang pernah dikeluarkan bisa ditambahkan lagi.
        (await AddAsync(manager, project.Id, userId, "member")).StatusCode.Should().Be(HttpStatusCode.Created);

        Convert.ToInt32(await fixture.ScalarAsync(
            "SELECT COUNT(*) FROM OUTBOX_MESSAGES WHERE TYPE LIKE :type AND PAYLOAD LIKE :payload",
            new OracleParameter("type", "%ProjectMember%DomainEvent%"),
            new OracleParameter("payload", $"%{userId}%"))).Should().Be(4, "tambah, ubah peran, keluarkan, tambah lagi");
    }

    [Fact]
    public async Task Manager_terakhir_tidak_bisa_dikeluarkan_atau_diturunkan()
    {
        var manager = fixture.ClientAs(A);
        var managerId = OracleProjectFixture.ManagerId(A);
        var project = await CreateProjectAsync(manager);

        (await manager.DeleteAsync(Members(project.Id, managerId))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await manager.PutAsJsonAsync(Members(project.Id, managerId), new { role = "member" })).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ListAsync(manager, project.Id)).Should().ContainSingle().Which.Role.Should().Be("manager");

        // Setelah ada manager kedua, manager pertama boleh dikeluarkan (oleh manager kedua).
        var secondId = NewUser("project.read", "project.member.manage");
        (await AddAsync(manager, project.Id, secondId, "manager")).StatusCode.Should().Be(HttpStatusCode.Created);
        (await fixture.ClientAs(A, secondId).DeleteAsync(Members(project.Id, managerId))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await manager.GetAsync($"/api/v1/projects/{project.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await fixture.ClientAs(A, secondId).DeleteAsync(Members(project.Id, secondId))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Hanya_manager_proyek_atau_admin_tenant_yang_boleh_mengelola_anggota()
    {
        var manager = fixture.ClientAs(A);
        var project = await CreateProjectAsync(manager);
        var target = NewUser();

        // Anggota biasa yang memegang permission kelola anggota, tetapi bukan manager proyek ini.
        var plainMemberId = NewUser("project.read", "project.member.manage");
        (await AddAsync(manager, project.Id, plainMemberId)).StatusCode.Should().Be(HttpStatusCode.Created);
        var plainMember = fixture.ClientAs(A, plainMemberId);
        (await AddAsync(plainMember, project.Id, target)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await plainMember.DeleteAsync(Members(project.Id, OracleProjectFixture.ManagerId(A)))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Manager proyek lain (bukan anggota proyek ini) dan user tanpa permission kelola anggota.
        var otherManager = fixture.ClientAs(A, NewUser("project.read", "project.create", "project.member.manage"));
        (await AddAsync(otherManager, project.Id, target)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AddAsync(fixture.MemberClient(A), project.Id, target)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Admin tenant mengelola anggota tanpa menjadi anggota.
        var admin = fixture.AdminClient(A);
        (await AddAsync(admin, project.Id, target)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await admin.PutAsJsonAsync(Members(project.Id, target), new { role = "manager" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.DeleteAsync(Members(project.Id, plainMemberId))).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Hanya_manager_proyek_yang_boleh_mengubah_proyek_dan_perubahan_anggota_menaikkan_versi()
    {
        var manager = fixture.ClientAs(A);
        var project = await CreateProjectAsync(manager);
        var url = $"/api/v1/projects/{project.Id}";
        object body = new { name = "Diubah", status = "active", priority = "medium" };

        HttpRequestMessage Put(long rowVersion)
        {
            var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body) };
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{rowVersion}\"");
            return request;
        }

        // Anggota berperan member yang memegang project.update tetap tidak boleh mengubah proyek.
        var memberId = NewUser("project.read", "project.update");
        (await AddAsync(manager, project.Id, memberId)).StatusCode.Should().Be(HttpStatusCode.Created);
        var afterAdd = (await manager.GetFromJsonAsync<ProjectBody>(url))!;
        afterAdd.RowVersion.Should().Be(project.RowVersion + 1, "perubahan anggota menaikkan versi proyek");

        (await fixture.ClientAs(A, memberId).SendAsync(Put(afterAdd.RowVersion))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await manager.SendAsync(Put(project.RowVersion))).StatusCode.Should().Be(HttpStatusCode.Conflict, "versi sebelum anggota ditambah sudah usang");
        (await manager.SendAsync(Put(afterAdd.RowVersion))).StatusCode.Should().Be(HttpStatusCode.OK);

        // Proyek yang diarsipkan: anggotanya tidak bisa diubah lagi.
        var current = (await manager.GetFromJsonAsync<ProjectBody>(url))!;
        var archive = new HttpRequestMessage(HttpMethod.Delete, url);
        archive.Headers.TryAddWithoutValidation("If-Match", $"\"{current.RowVersion}\"");
        (await manager.SendAsync(archive)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await AddAsync(manager, project.Id, NewUser())).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await manager.DeleteAsync(Members(project.Id, memberId))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Read_model_user_diisi_dari_event_dan_user_nonaktif_tidak_bisa_ditambahkan()
    {
        var manager = fixture.ClientAs(A);
        var project = await CreateProjectAsync(manager);
        var userId = NewUser();
        var t0 = DateTimeOffset.UtcNow;

        // Belum dikenal read model: tetap bisa ditambahkan, nama kosong.
        (await AddAsync(manager, project.Id, userId)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await ListAsync(manager, project.Id)).Single(m => m.UserId == userId)
            .Should().Match<MemberBody>(m => m.FullName == null && m.IsActive == null);

        var created = new UserCreated(Guid.NewGuid(), t0, A, userId, "sari@contoh.com", "Sari Dewi");
        await PublishAsync(created);
        await WaitForUserRefAsync(userId, m => m.FullName == "Sari Dewi", manager, project.Id);

        // Event yang sama dua kali dan event lama yang tiba belakangan tidak mengubah apa pun.
        await PublishAsync(new UserUpdated(Guid.NewGuid(), t0.AddMinutes(2), A, userId, "sari@contoh.com", "Sari D. Putri", true));
        await WaitForUserRefAsync(userId, m => m.FullName == "Sari D. Putri", manager, project.Id);
        await PublishAsync(created);
        await PublishAsync(new UserUpdated(Guid.NewGuid(), t0.AddMinutes(1), A, userId, "lama@contoh.com", "Nama Lama", true));

        await PublishAsync(new UserDeactivated(Guid.NewGuid(), t0.AddMinutes(3), A, userId));
        await WaitForUserRefAsync(userId, m => m.IsActive == false, manager, project.Id);
        (await ListAsync(manager, project.Id)).Single(m => m.UserId == userId)
            .Should().Match<MemberBody>(m => m.FullName == "Sari D. Putri" && m.Email == "sari@contoh.com");

        Convert.ToInt32(await fixture.ScalarAsync(
            "SELECT COUNT(*) FROM USER_REF WHERE USER_ID = :usr", new OracleParameter("usr", OracleDbType.Raw) { Value = userId.ToByteArray() }))
            .Should().Be(1);
        Convert.ToInt32(await fixture.ScalarAsync(
            "SELECT COUNT(*) FROM PROCESSED_EVENTS WHERE EVENT_ID = :id", new OracleParameter("id", OracleDbType.Raw) { Value = created.EventId.ToByteArray() }))
            .Should().Be(1);

        // User yang diketahui nonaktif tidak bisa ditambahkan ke proyek lain; read model tenant lain tidak terpengaruh.
        var other = await CreateProjectAsync(manager);
        (await AddAsync(manager, other.Id, userId)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var inOtherTenant = await CreateProjectAsync(fixture.ClientAs(B));
        (await AddAsync(fixture.ClientAs(B), inOtherTenant.Id, userId)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await ListAsync(fixture.ClientAs(B), inOtherTenant.Id)).Single(m => m.UserId == userId).FullName.Should().BeNull();
    }
}
