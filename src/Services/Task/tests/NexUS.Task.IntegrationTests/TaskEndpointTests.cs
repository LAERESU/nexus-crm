using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NexUS.Tasks.Application.Abstractions;
using NexUS.Tasks.Domain.Tasks;
using NexUS.Tasks.Infrastructure.Persistence;
using Oracle.ManagedDataAccess.Client;

namespace NexUS.Tasks.IntegrationTests;

[Collection(OracleCollection.Name)]
[Trait("Category", "Oracle")]
public class TaskEndpointTests(OracleTaskFixture fixture)
{
    private const long A = OracleTaskFixture.TenantA;

    private sealed record TaskBody(
        Guid Id, Guid ProjectId, Guid? ParentTaskId, string Title, string? Description, string Status, string Priority,
        DateOnly? DueDate, Guid ReporterId, List<Guid> AssigneeIds, long RowVersion);

    private sealed record Paged(List<TaskBody> Items, int Page, int Size, long TotalCount);

    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, Guid projectId, string title = "Tugas", Guid? parent = null, string? dueDate = null) =>
        client.PostAsJsonAsync("/api/v1/tasks", new { projectId, parentTaskId = parent, title, description = "Isi", priority = "high", dueDate });

    private static async Task<TaskBody> CreatedAsync(HttpClient client, Guid projectId, string title = "Tugas", Guid? parent = null, string? dueDate = null)
    {
        var response = await CreateAsync(client, projectId, title, parent, dueDate);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TaskBody>())!;
    }

    private static HttpRequestMessage WithIfMatch(HttpMethod method, string url, long rowVersion, object? body = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{rowVersion}\"");
        return request;
    }

    private static async Task<List<TaskBody>> ListAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<Paged>($"/api/v1/tasks?size=100&{query}"))!.Items;

    [Fact]
    public async Task Hanya_anggota_proyek_yang_bisa_membuat_dan_melihat_tugas_proyek_itu()
    {
        var manager = fixture.NewUser(A);
        var outsiderId = fixture.NewUser(A);
        var project = await fixture.SeedProjectAsync(A, members: (manager, "MANAGER"));
        var client = fixture.ClientAs(A, manager);
        var outsider = fixture.ClientAs(A, outsiderId);

        var created = await CreateAsync(client, project, "Desain halaman");
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var task = (await created.Content.ReadFromJsonAsync<TaskBody>())!;
        created.Headers.Location!.ToString().Should().EndWith($"/api/v1/tasks/{task.Id}");
        task.Should().Match<TaskBody>(t => t.Status == "todo" && t.Priority == "high" && t.ReporterId == manager && t.ProjectId == project);

        (await CreateAsync(outsider, project)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await outsider.GetAsync($"/api/v1/tasks/{task.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await outsider.GetAsync($"/api/v1/tasks?projectId={project}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ListAsync(outsider, "")).Should().NotContain(t => t.Id == task.Id, "tanpa filter proyek hanya proyek yang diikuti");

        // Setelah menjadi anggota, tugasnya terlihat.
        await fixture.AddMemberAsync(A, project, outsiderId, "MEMBER");
        (await outsider.GetAsync($"/api/v1/tasks/{task.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ListAsync(outsider, "")).Should().Contain(t => t.Id == task.Id);

        // Pengamat hanya melihat; proyek yang belum dikenal 404; admin (project.all.read) melihat tanpa menjadi anggota.
        var viewer = fixture.NewUser(A);
        await fixture.AddMemberAsync(A, project, viewer, "VIEWER");
        (await fixture.ClientAs(A, viewer).GetAsync($"/api/v1/tasks/{task.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CreateAsync(fixture.ClientAs(A, viewer), project)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CreateAsync(client, Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var admin = fixture.NewUser(A, "task.read", "project.all.read");
        (await fixture.ClientAs(A, admin).GetAsync($"/api/v1/tasks/{task.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Tenant lain tidak melihat tugas ini sama sekali (user yang sama, permission lengkap di tenant B).
        fixture.Grant(OracleTaskFixture.TenantB, manager, OracleTaskFixture.Worker);
        (await fixture.ClientAs(OracleTaskFixture.TenantB, manager).GetAsync($"/api/v1/tasks/{task.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Subtask_dari_subtask_ditolak_dan_subtask_harus_di_proyek_yang_sama()
    {
        var user = fixture.NewUser(A);
        var project = await fixture.SeedProjectAsync(A, members: (user, "MEMBER"));
        var other = await fixture.SeedProjectAsync(A, members: (user, "MEMBER"));
        var client = fixture.ClientAs(A, user);

        var parent = await CreatedAsync(client, project, "Induk");
        var child = await CreatedAsync(client, project, "Anak", parent.Id);
        child.ParentTaskId.Should().Be(parent.Id);

        (await CreateAsync(client, project, "Cucu", child.Id)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CreateAsync(client, other, "Lintas proyek", parent.Id)).StatusCode.Should().Be(HttpStatusCode.NotFound, "induk di proyek lain dianggap tidak ada");
        (await CreateAsync(client, project, "Induk hilang", Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_dengan_If_Match_dan_hapus_ikut_menghapus_subtask()
    {
        var user = fixture.NewUser(A);
        var project = await fixture.SeedProjectAsync(A, members: (user, "MEMBER"));
        var client = fixture.ClientAs(A, user);
        var created = await CreatedAsync(client, project, "Awal");
        var child = await CreatedAsync(client, project, "Anak", created.Id);
        var url = $"/api/v1/tasks/{created.Id}";
        // Subtask baru menaikkan versi induk; versi terbaru dibaca ulang.
        var task = (await client.GetFromJsonAsync<TaskBody>(url))!;
        task.RowVersion.Should().Be(created.RowVersion + 1);

        var updated = await client.SendAsync(WithIfMatch(HttpMethod.Put, url, task.RowVersion,
            new { title = "Diubah", description = (string?)null, priority = "urgent", dueDate = "2026-12-31" }));
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await updated.Content.ReadFromJsonAsync<TaskBody>())!;
        body.Should().Match<TaskBody>(t => t.Title == "Diubah" && t.Priority == "urgent" && t.Description == null && t.DueDate == new DateOnly(2026, 12, 31));
        body.RowVersion.Should().Be(task.RowVersion + 1);

        (await client.SendAsync(WithIfMatch(HttpMethod.Put, url, task.RowVersion, new { title = "Basi", priority = "low" })))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.PutAsJsonAsync(url, new { title = "X", priority = "low" })).StatusCode.Should().Be(HttpStatusCode.BadRequest, "If-Match wajib");
        (await client.SendAsync(WithIfMatch(HttpMethod.Put, url, body.RowVersion, new { title = "X", priority = "penting" })))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await client.SendAsync(WithIfMatch(HttpMethod.Delete, url, body.RowVersion))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/v1/tasks/{child.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "subtask ikut dihapus");
        Convert.ToInt32(await fixture.ScalarAsync("SELECT IS_DELETED FROM TASKS WHERE PUBLIC_ID = :id", OracleTaskFixture.Raw("id", child.Id)))
            .Should().Be(1, "soft delete");
    }

    [Fact]
    public async Task Tugas_proyek_yang_diarsipkan_hanya_bisa_dilihat()
    {
        var user = fixture.NewUser(A);
        var project = await fixture.SeedProjectAsync(A, members: (user, "MANAGER"));
        var client = fixture.ClientAs(A, user);
        var task = await CreatedAsync(client, project);

        await fixture.ExecuteAsync("UPDATE PROJECT_REF SET IS_ARCHIVED = 1 WHERE PROJECT_ID = :p", OracleTaskFixture.Raw("p", project));

        (await client.GetAsync($"/api/v1/tasks/{task.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CreateAsync(client, project)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await client.SendAsync(WithIfMatch(HttpMethod.Put, $"/api/v1/tasks/{task.Id}", task.RowVersion, new { title = "X", priority = "low" })))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await client.SendAsync(WithIfMatch(HttpMethod.Delete, $"/api/v1/tasks/{task.Id}", task.RowVersion)))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task List_mendukung_filter_proyek_status_jatuh_tempo_assignee_dan_paging()
    {
        var user = fixture.NewUser(A);
        var project = await fixture.SeedProjectAsync(A, members: (user, "MEMBER"));
        var client = fixture.ClientAs(A, user);
        var early = await CreatedAsync(client, project, "Awal", dueDate: "2026-12-01");
        var late = await CreatedAsync(client, project, "Akhir", dueDate: "2026-12-20");
        var none = await CreatedAsync(client, project, "Tanpa tanggal");

        (await ListAsync(client, $"projectId={project}")).Select(t => t.Id).Should().Equal(early.Id, late.Id, none.Id);
        (await ListAsync(client, $"projectId={project}&dueBefore=2026-12-01")).Select(t => t.Id).Should().Equal(early.Id);
        (await ListAsync(client, $"projectId={project}&due_before=2026-12-01")).Select(t => t.Id).Should().Equal(early.Id);
        (await ListAsync(client, $"projectId={project}&status=todo")).Should().HaveCount(3);
        (await ListAsync(client, $"projectId={project}&status=done")).Should().BeEmpty();

        var assignee = Guid.NewGuid();
        await fixture.ExecuteAsync(
            "INSERT INTO TASK_ASSIGNEES (TENANT_ID, TASK_ID, USER_ID, ASSIGNED_AT, ASSIGNED_BY, CREATED_AT, CREATED_BY) " +
            "SELECT TENANT_ID, ID, :u, SYSTIMESTAMP, :b, SYSTIMESTAMP, 0 FROM TASKS WHERE PUBLIC_ID = :id",
            OracleTaskFixture.Raw("u", assignee), OracleTaskFixture.Raw("b", user), OracleTaskFixture.Raw("id", late.Id));
        var assigned = await ListAsync(client, $"projectId={project}&assigneeId={assignee}");
        assigned.Should().ContainSingle().Which.Should().Match<TaskBody>(t => t.Id == late.Id && t.AssigneeIds.Contains(assignee));

        var page = await client.GetFromJsonAsync<Paged>($"/api/v1/tasks?projectId={project}&page=2&size=2");
        page!.TotalCount.Should().Be(3);
        page.Items.Should().ContainSingle().Which.Id.Should().Be(none.Id, "tanpa jatuh tempo di akhir");

        (await client.GetAsync("/api/v1/tasks?status=selesai")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync("/api/v1/tasks?size=101")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Permission_tulis_berscope_proyek_lain_tidak_berlaku_dan_bukan_anggota_tidak_tahu_status_arsip()
    {
        var user = Guid.NewGuid();
        var other = await fixture.SeedProjectAsync(A);
        var project = await fixture.SeedProjectAsync(A, members: (user, "MEMBER"));
        // task.create hanya untuk proyek lain; anggota proyek ini.
        fixture.GrantScoped(A, user, ("task.read", "global", null), ("task.create", "project", other));
        (await CreateAsync(fixture.ClientAs(A, user), project)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var outsider = fixture.NewUser(A);
        var archived = await fixture.SeedProjectAsync(A, archived: true);
        (await CreateAsync(fixture.ClientAs(A, outsider), archived)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "bukan 422 arsip");
    }

    [Fact]
    public async Task Membuat_subtask_menaikkan_versi_induk_sehingga_hapus_induk_yang_basi_bentrok()
    {
        var user = fixture.NewUser(A);
        var project = await fixture.SeedProjectAsync(A, members: (user, "MEMBER"));
        var client = fixture.ClientAs(A, user);
        var parent = await CreatedAsync(client, project, "Induk");

        await CreatedAsync(client, project, "Anak", parent.Id);

        (await client.SendAsync(WithIfMatch(HttpMethod.Delete, $"/api/v1/tasks/{parent.Id}", parent.RowVersion)))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "subtask baru menaikkan versi induk");
    }

    [Fact]
    public async Task Endpoint_dijaga_permission()
    {
        var user = fixture.NewUser(A, "task.read", "project.read");
        var project = await fixture.SeedProjectAsync(A, members: (user, "MEMBER"));

        (await fixture.Factory.CreateClient().GetAsync("/api/v1/tasks")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CreateAsync(fixture.ClientAs(A, user), project)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "tanpa task.create");
        (await fixture.ClientAs(A, Guid.NewGuid()).GetAsync("/api/v1/tasks")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Query_list_memakai_index_tenant_proyek_status()
    {
        // Data cukup banyak (tenant khusus) agar optimizer memilih index, lalu statistik dikumpulkan.
        await fixture.ExecuteAsync(
            "INSERT INTO TASKS (TENANT_ID, PROJECT_ID, TITLE, STATUS, PRIORITY, REPORTER_ID, CREATED_AT, CREATED_BY) " +
            "SELECT 99, HEXTORAW(LPAD(TO_CHAR(MOD(LEVEL, 50)), 32, '0')), 'T' || LEVEL, " +
            "CASE MOD(LEVEL, 6) WHEN 0 THEN 'TODO' WHEN 1 THEN 'IN_PROGRESS' WHEN 2 THEN 'IN_REVIEW' WHEN 3 THEN 'BLOCKED' WHEN 4 THEN 'DONE' ELSE 'CANCELLED' END, " +
            "'MEDIUM', HEXTORAW('00'), SYSTIMESTAMP, 0 FROM DUAL CONNECT BY LEVEL <= 20000");
        await fixture.ExecuteAsync("BEGIN DBMS_STATS.GATHER_TABLE_STATS(USER, 'TASKS', CASCADE => TRUE); END;");

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<TaskReadService>();
        var query = reader.ListQuery(new TaskFilter(Guid.NewGuid(), null, TaskItemStatus.InProgress, null, null, 1, 20))
            .OrderBy(t => t.DueDate).ThenBy(t => t.Id).Skip(0).Take(20);
        // Provider Oracle membungkus SQL dalam blok PL/SQL: l_sql := '<sql>'; dbms_sql.parse(...)
        var script = query.ToQueryString();
        var start = script.IndexOf("l_sql := '", StringComparison.Ordinal) + "l_sql := '".Length;
        var end = script.IndexOf("';", start, StringComparison.Ordinal);
        var sql = script[start..end].Replace("''", "'", StringComparison.Ordinal);

        var statementId = $"nx032{Guid.NewGuid():N}"[..20];
        try
        {
            await fixture.ExecuteAsync($"EXPLAIN PLAN SET STATEMENT_ID = '{statementId}' FOR {sql}");
        }
        catch (OracleException ex)
        {
            throw new InvalidOperationException($"EXPLAIN PLAN gagal untuk SQL:\n{query.ToQueryString()}", ex);
        }

        var usesIndex = Convert.ToInt32(await fixture.ScalarAsync(
            "SELECT COUNT(*) FROM PLAN_TABLE WHERE STATEMENT_ID = :s AND OBJECT_NAME = 'IX_TASKS_TNT_PRJ_STATUS'",
            new OracleParameter("s", statementId)));

        usesIndex.Should().BeGreaterThan(0, $"rencana eksekusi harus memakai IX_TASKS_TNT_PRJ_STATUS:\n{sql}");
    }
}
