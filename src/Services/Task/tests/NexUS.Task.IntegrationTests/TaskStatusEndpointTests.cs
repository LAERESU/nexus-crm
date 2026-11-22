using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace NexUS.Tasks.IntegrationTests;

/// <summary>NX-033: <c>PATCH /api/v1/tasks/{id}/status</c>, riwayat status, dan <c>COMPLETED_AT</c>.</summary>
[Collection(OracleCollection.Name)]
[Trait("Category", "Oracle")]
public class TaskStatusEndpointTests(OracleTaskFixture fixture)
{
    private const long A = OracleTaskFixture.TenantA;

    private sealed record TaskBody(Guid Id, string Status, DateTimeOffset? CompletedAt, long RowVersion);

    private async Task<(HttpClient Client, Guid User, Guid Project, TaskBody Task)> NewTaskAsync(string role = "MEMBER")
    {
        var user = fixture.NewUser(A);
        var project = await fixture.SeedProjectAsync(A, members: (user, role));
        var client = fixture.ClientAs(A, user);
        var response = await client.PostAsJsonAsync("/api/v1/tasks", new { projectId = project, title = "Tugas status" });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (client, user, project, (await response.Content.ReadFromJsonAsync<TaskBody>())!);
    }

    private static Task<HttpResponseMessage> PatchAsync(HttpClient client, Guid id, string status, long? ifMatch = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/tasks/{id}/status") { Content = JsonContent.Create(new { status }) };
        if (ifMatch is { } version) request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        return client.SendAsync(request);
    }

    private static async Task<TaskBody> MovedAsync(HttpClient client, Guid id, string status)
    {
        var response = await PatchAsync(client, id, status);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TaskBody>())!;
    }

    private async Task<List<string>> HistoryAsync(Guid taskId)
    {
        var count = Convert.ToInt32(await fixture.ScalarAsync(
            "SELECT COUNT(*) FROM TASK_STATUS_HISTORY h JOIN TASKS t ON t.ID = h.TASK_ID WHERE t.PUBLIC_ID = :id",
            OracleTaskFixture.Raw("id", taskId)));
        var rows = new List<string>();
        for (var i = 1; i <= count; i++)
        {
            rows.Add((string)(await fixture.ScalarAsync(
                "SELECT FROM_STATUS || '>' || TO_STATUS FROM (SELECT h.FROM_STATUS, h.TO_STATUS, ROW_NUMBER() OVER (ORDER BY h.ID) RN " +
                "FROM TASK_STATUS_HISTORY h JOIN TASKS t ON t.ID = h.TASK_ID WHERE t.PUBLIC_ID = :id) WHERE RN = " + i,
                OracleTaskFixture.Raw("id", taskId)))!);
        }

        return rows;
    }

    [Fact]
    public async Task Alur_utama_mencatat_riwayat_dan_mengisi_completed_at()
    {
        var (client, user, _, task) = await NewTaskAsync();

        await MovedAsync(client, task.Id, "in_progress");
        await MovedAsync(client, task.Id, "in_review");
        var done = await MovedAsync(client, task.Id, "done");
        done.Status.Should().Be("done");
        done.CompletedAt.Should().NotBeNull();
        done.RowVersion.Should().Be(task.RowVersion + 3);

        var reopened = await MovedAsync(client, task.Id, "in_progress");
        reopened.CompletedAt.Should().BeNull("dibuka ulang");

        (await HistoryAsync(task.Id)).Should().Equal("TODO>IN_PROGRESS", "IN_PROGRESS>IN_REVIEW", "IN_REVIEW>DONE", "DONE>IN_PROGRESS");
        Convert.ToInt32(await fixture.ScalarAsync(
            "SELECT COUNT(*) FROM TASK_STATUS_HISTORY h JOIN TASKS t ON t.ID = h.TASK_ID WHERE t.PUBLIC_ID = :id AND h.CHANGED_BY = :u AND h.TENANT_ID = :tnt",
            OracleTaskFixture.Raw("id", task.Id), OracleTaskFixture.Raw("u", user), new Oracle.ManagedDataAccess.Client.OracleParameter("tnt", A)))
            .Should().Be(4, "pengubah dan tenant tercatat");
        Convert.ToInt32(await fixture.ScalarAsync(
            "SELECT COUNT(*) FROM OUTBOX_MESSAGES WHERE TYPE LIKE '%TaskStatusChanged%' AND PAYLOAD LIKE :p",
            new Oracle.ManagedDataAccess.Client.OracleParameter("p", $"%{task.Id}%"))).Should().Be(4, "event TaskStatusChanged masuk outbox");
    }

    [Fact]
    public async Task Transisi_tidak_valid_dijawab_422_dengan_pesan_jelas_dan_tidak_tercatat()
    {
        var (client, _, _, task) = await NewTaskAsync();

        var response = await PatchAsync(client, task.Id, "done");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>())!;
        problem.Detail.Should().Be("Status tugas tidak bisa diubah dari todo ke done. Dari todo hanya bisa ke: in_progress, blocked, cancelled.");
        (await HistoryAsync(task.Id)).Should().BeEmpty();

        (await PatchAsync(client, task.Id, "selesai")).StatusCode.Should().Be(HttpStatusCode.BadRequest, "status tidak dikenal");
    }

    [Fact]
    public async Task Status_sama_tidak_dicatat_dan_if_match_basi_bentrok()
    {
        var (client, _, _, task) = await NewTaskAsync();
        var blocked = await MovedAsync(client, task.Id, "blocked");

        (await MovedAsync(client, task.Id, "blocked")).RowVersion.Should().Be(blocked.RowVersion, "status sama tidak mengubah apa pun");
        (await HistoryAsync(task.Id)).Should().Equal("TODO>BLOCKED");

        (await PatchAsync(client, task.Id, "todo", ifMatch: task.RowVersion)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PatchAsync(client, task.Id, "todo", ifMatch: blocked.RowVersion)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Pengamat_dan_proyek_arsip_tidak_bisa_mengubah_status()
    {
        var (client, _, project, task) = await NewTaskAsync();

        var viewer = fixture.NewUser(A);
        await fixture.ExecuteAsync(
            "INSERT INTO PROJECT_MEMBER_REFS (TENANT_ID, PROJECT_ID, USER_ID, PROJECT_ROLE, LAST_EVENT_AT, CREATED_AT, CREATED_BY) " +
            "VALUES (:tnt, :p, :u, 'VIEWER', SYSTIMESTAMP, SYSTIMESTAMP, 0)",
            new Oracle.ManagedDataAccess.Client.OracleParameter("tnt", A), OracleTaskFixture.Raw("p", project), OracleTaskFixture.Raw("u", viewer));
        (await PatchAsync(fixture.ClientAs(A, viewer), task.Id, "in_progress")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await fixture.ExecuteAsync("UPDATE PROJECT_REF SET IS_ARCHIVED = 1 WHERE PROJECT_ID = :p", OracleTaskFixture.Raw("p", project));
        (await PatchAsync(client, task.Id, "in_progress")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await HistoryAsync(task.Id)).Should().BeEmpty();
    }
}
