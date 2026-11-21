using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexUS.BuildingBlocks.Messaging;
using NexUS.BuildingBlocks.Messaging.Contracts;

namespace NexUS.Tasks.IntegrationTests;

/// <summary>Read model proyek diisi dari event Project Service lewat RabbitMQ sungguhan.</summary>
[Collection(OracleCollection.Name)]
[Trait("Category", "Oracle")]
public class ProjectEventTests(OracleTaskFixture fixture)
{
    private const long A = OracleTaskFixture.TenantA;

    private sealed record TaskBody(Guid Id, long RowVersion);

    private async Task PublishAsync(IIntegrationEvent integrationEvent)
    {
        await fixture.WaitForQueueAsync($"task.{EventBusTopology.RoutingKey(integrationEvent.GetType())}");
        await fixture.Factory.Services.GetRequiredService<IEventPublisher>().PublishAsync(integrationEvent);
    }

    /// <summary>Mengulang permintaan sampai event diproses consumer dan jawabannya sesuai.</summary>
    private static async Task<HttpResponseMessage> EventuallyAsync(Func<Task<HttpResponseMessage>> send, HttpStatusCode expected)
    {
        HttpResponseMessage response = default!;
        for (var attempt = 0; attempt < 120; attempt++)
        {
            response = await send();
            if (response.StatusCode == expected) return response;
            await Task.Delay(250);
        }

        return response;
    }

    private static Task<HttpResponseMessage> CreateTask(HttpClient client, Guid projectId) =>
        client.PostAsJsonAsync("/api/v1/tasks", new { projectId, title = "Dari event" });

    [Fact]
    public async Task Event_proyek_dan_anggota_mengatur_akses_tugas()
    {
        var owner = fixture.NewUser(A);
        var member = fixture.NewUser(A);
        var projectId = Guid.NewGuid();
        var t0 = DateTimeOffset.UtcNow;
        var ownerClient = fixture.ClientAs(A, owner);
        var memberClient = fixture.ClientAs(A, member);

        // Pembuat proyek otomatis manager.
        await PublishAsync(new ProjectCreated(Guid.NewGuid(), t0, A, projectId, "EVT", "Proyek Event", owner));
        var created = await EventuallyAsync(() => CreateTask(ownerClient, projectId), HttpStatusCode.Created);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var task = (await created.Content.ReadFromJsonAsync<TaskBody>())!;

        // Anggota ditambahkan lalu dikeluarkan.
        (await memberClient.GetAsync($"/api/v1/tasks/{task.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var added = new MemberAdded(Guid.NewGuid(), t0.AddSeconds(1), A, projectId, member, "member");
        await PublishAsync(added);
        (await EventuallyAsync(() => memberClient.GetAsync($"/api/v1/tasks/{task.Id}"), HttpStatusCode.OK)).StatusCode
            .Should().Be(HttpStatusCode.OK);

        await PublishAsync(new MemberRemoved(Guid.NewGuid(), t0.AddSeconds(2), A, projectId, member));
        (await EventuallyAsync(() => memberClient.GetAsync($"/api/v1/tasks/{task.Id}"), HttpStatusCode.Forbidden)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        // Event MemberAdded yang sama dikirim ulang (lebih lama dari pengeluaran): tidak mengembalikan akses.
        await PublishAsync(added);
        await PublishAsync(new MemberRoleChanged(Guid.NewGuid(), t0.AddMilliseconds(1500), A, projectId, member, "manager"));
        await Task.Delay(TimeSpan.FromSeconds(2));
        (await memberClient.GetAsync($"/api/v1/tasks/{task.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Convert.ToInt32(await fixture.ScalarAsync(
            "SELECT COUNT(*) FROM PROCESSED_EVENTS WHERE EVENT_ID = :id", OracleTaskFixture.Raw("id", added.EventId))).Should().Be(1);

        // Proyek diarsipkan: tugas hanya bisa dilihat.
        await PublishAsync(new ProjectArchived(Guid.NewGuid(), t0.AddSeconds(3), A, projectId));
        (await EventuallyAsync(() => CreateTask(ownerClient, projectId), HttpStatusCode.UnprocessableEntity)).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ownerClient.GetAsync($"/api/v1/tasks/{task.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
