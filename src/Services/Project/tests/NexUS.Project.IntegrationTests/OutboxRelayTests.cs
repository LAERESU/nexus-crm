using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexUS.BuildingBlocks.Messaging;
using NexUS.BuildingBlocks.Messaging.Contracts;
using Oracle.ManagedDataAccess.Client;
using RabbitMQ.Client;

namespace NexUS.Project.IntegrationTests;

/// <summary>Relay outbox Project Service ke RabbitMQ sungguhan, dead-letter, dan parkir event tanpa antrean (NX-028).</summary>
[Collection(OracleCollection.Name)]
[Trait("Category", "Oracle")]
public class OutboxRelayTests(OracleProjectFixture fixture)
{
    private const long A = OracleProjectFixture.TenantA;

    private sealed record ProjectBody(Guid Id, string Code, long RowVersion);

    private async Task<(IConnection Connection, IChannel Channel)> OpenAsync()
    {
        var connection = await new ConnectionFactory { Uri = new Uri(fixture.RabbitConnectionString) }.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();
        await EventBusTopology.DeclareExchangeAsync(channel, default);
        return (connection, channel);
    }

    /// <summary>Antrean uji sementara yang diikat ke satu jenis event, seperti antrean consumer service lain.</summary>
    private static async Task<string> BindAsync(IChannel channel, string eventName)
    {
        var queue = (await channel.QueueDeclareAsync(string.Empty, durable: false, exclusive: true, autoDelete: true)).QueueName;
        await channel.QueueBindAsync(queue, EventBusTopology.Exchange, eventName);
        return queue;
    }

    /// <summary>Mengambil pesan dari antrean sampai ada yang memenuhi <paramref name="match"/>.</summary>
    private static async Task<BasicGetResult> WaitForMessageAsync(IChannel channel, string queue, Func<BasicGetResult, bool> match, int seconds = 120)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < until)
        {
            while (await channel.BasicGetAsync(queue, autoAck: true) is { } message)
                if (match(message)) return message;
            await Task.Delay(250);
        }

        throw new TimeoutException($"Pesan yang diharapkan tidak tiba di {queue}.");
    }

    private Task<object?> ProcessedAtAsync(string code) => fixture.ScalarAsync(
        "SELECT MAX(PROCESSED_AT) FROM OUTBOX_MESSAGES WHERE TYPE LIKE :type AND PAYLOAD LIKE :payload",
        new OracleParameter("type", "%ProjectCreatedDomainEvent%"),
        new OracleParameter("payload", $"%{code}%"));

    private async Task<ProjectBody> CreateProjectAsync()
    {
        var response = await fixture.ClientAs(A).PostAsJsonAsync("/api/v1/projects", new { code = $"R{Guid.NewGuid():N}"[..12], name = "Proyek Relay" });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ProjectBody>())!;
    }

    [Fact]
    public async Task Proyek_tetap_tersimpan_saat_RabbitMQ_mati_dan_eventnya_terkirim_setelah_hidup()
    {
        var (connection, channel) = await OpenAsync();
        await using var _ = connection;
        await using var __ = channel;
        var queue = await BindAsync(channel, nameof(ProjectCreated));

        ProjectBody project;
        await fixture.PauseBrokerAsync();
        try
        {
            project = await CreateProjectAsync(); // database tetap menerima perubahan
            await Task.Delay(TimeSpan.FromSeconds(5));
            (await ProcessedAtAsync(project.Code)).Should().Be(DBNull.Value, "event tertunda di outbox selama broker mati");
        }
        finally
        {
            await fixture.UnpauseBrokerAsync();
        }

        var message = await WaitForMessageAsync(channel, queue, m =>
            JsonSerializer.Deserialize<ProjectCreated>(Encoding.UTF8.GetString(m.Body.Span), EventBusTopology.Json)!.ProjectId == project.Id);
        var received = JsonSerializer.Deserialize<ProjectCreated>(Encoding.UTF8.GetString(message.Body.Span), EventBusTopology.Json)!;
        received.Should().Match<ProjectCreated>(e =>
            e.Code == project.Code && e.TenantId == A && e.OwnerId == OracleProjectFixture.ManagerId(A));
        message.BasicProperties.MessageId.Should().Be(received.EventId.ToString("D"));

        for (var attempt = 0; attempt < 40 && await ProcessedAtAsync(project.Code) is DBNull; attempt++) await Task.Delay(250);
        (await ProcessedAtAsync(project.Code)).Should().NotBe(DBNull.Value);
    }

    [Fact]
    public async Task Event_anggota_dan_arsip_dipublikasikan_dengan_kontraknya()
    {
        var (connection, channel) = await OpenAsync();
        await using var _ = connection;
        await using var __ = channel;
        var added = await BindAsync(channel, nameof(MemberAdded));
        var archived = await BindAsync(channel, nameof(ProjectArchived));

        var manager = fixture.ClientAs(A);
        var project = await CreateProjectAsync();
        var userId = Guid.NewGuid();
        (await manager.PostAsJsonAsync($"/api/v1/projects/{project.Id}/members", new { userId, role = "viewer" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        var current = (await manager.GetFromJsonAsync<ProjectBody>($"/api/v1/projects/{project.Id}"))!;
        var archive = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/projects/{project.Id}");
        archive.Headers.TryAddWithoutValidation("If-Match", $"\"{current.RowVersion}\"");
        (await manager.SendAsync(archive)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var member = await WaitForMessageAsync(channel, added, m =>
            JsonSerializer.Deserialize<MemberAdded>(Encoding.UTF8.GetString(m.Body.Span), EventBusTopology.Json)!.ProjectId == project.Id);
        JsonSerializer.Deserialize<MemberAdded>(Encoding.UTF8.GetString(member.Body.Span), EventBusTopology.Json)!
            .Should().Match<MemberAdded>(e => e.UserId == userId && e.Role == "viewer");
        await WaitForMessageAsync(channel, archived, m =>
            JsonSerializer.Deserialize<ProjectArchived>(Encoding.UTF8.GetString(m.Body.Span), EventBusTopology.Json)!.ProjectId == project.Id);
    }

    [Fact]
    public async Task Event_tanpa_antrean_penerima_diparkir_dan_tidak_menahan_relay()
    {
        var (connection, channel) = await OpenAsync();
        await using var _ = connection;
        await using var __ = channel;

        // Tidak ada antrean yang diikat ke ProjectCreated: event diparkir di nexus.unrouted, outbox tetap ditandai terkirim.
        var project = await CreateProjectAsync();

        await WaitForMessageAsync(channel, EventBusTopology.UnroutedQueue, m =>
            m.BasicProperties.Type == nameof(ProjectCreated)
            && JsonSerializer.Deserialize<ProjectCreated>(Encoding.UTF8.GetString(m.Body.Span), EventBusTopology.Json)!.ProjectId == project.Id);
        for (var attempt = 0; attempt < 40 && await ProcessedAtAsync(project.Code) is DBNull; attempt++) await Task.Delay(250);
        (await ProcessedAtAsync(project.Code)).Should().NotBe(DBNull.Value);
    }

    [Fact]
    public async Task Event_yang_selalu_gagal_diproses_dipindah_ke_dead_letter_setelah_batas_percobaan()
    {
        var (connection, channel) = await OpenAsync();
        await using var _ = connection;
        await using var __ = channel;
        const string queue = "project.UserCreated";
        await fixture.WaitForQueueAsync(queue);

        // Email melebihi panjang kolom USER_REF: penyimpanan selalu gagal.
        var poison = new UserCreated(Guid.NewGuid(), DateTimeOffset.UtcNow, A, Guid.NewGuid(), new string('x', 400) + "@contoh.com", "Racun");
        await fixture.Factory.Services.GetRequiredService<IEventPublisher>().PublishAsync(poison);

        var dead = await WaitForMessageAsync(channel, EventBusTopology.DeadLetterQueue(queue),
            m => m.BasicProperties.MessageId == poison.EventId.ToString("D"), seconds: 90);
        Convert.ToInt32(dead.BasicProperties.Headers![EventBusTopology.AttemptsHeader]).Should().Be(5);
        dead.BasicProperties.Headers.Should().ContainKey("x-nexus-last-error");

        Convert.ToInt32(await fixture.ScalarAsync(
            "SELECT COUNT(*) FROM USER_REF WHERE USER_ID = :usr",
            new OracleParameter("usr", OracleDbType.Raw) { Value = poison.UserId.ToByteArray() })).Should().Be(0);
    }

    [Fact]
    public async Task Event_yang_sama_dikonsumsi_dua_kali_tidak_menghasilkan_efek_ganda()
    {
        var created = new UserCreated(Guid.NewGuid(), DateTimeOffset.UtcNow, A, Guid.NewGuid(), "ganda@contoh.com", "Ganda");
        await fixture.WaitForQueueAsync("project.UserCreated");
        var publisher = fixture.Factory.Services.GetRequiredService<IEventPublisher>();
        await publisher.PublishAsync(created);
        await publisher.PublishAsync(created);

        // Penanda: event berikutnya selesai diproses berarti kedua salinan di depannya sudah lewat.
        var marker = new UserCreated(Guid.NewGuid(), DateTimeOffset.UtcNow, A, Guid.NewGuid(), "penanda@contoh.com", "Penanda");
        await publisher.PublishAsync(marker);
        for (var attempt = 0; attempt < 120; attempt++)
        {
            if (Convert.ToInt32(await fixture.ScalarAsync(
                    "SELECT COUNT(*) FROM USER_REF WHERE USER_ID = :usr",
                    new OracleParameter("usr", OracleDbType.Raw) { Value = marker.UserId.ToByteArray() })) == 1) break;
            await Task.Delay(250);
        }

        Convert.ToInt32(await fixture.ScalarAsync(
            "SELECT COUNT(*) FROM PROCESSED_EVENTS WHERE EVENT_ID = :id",
            new OracleParameter("id", OracleDbType.Raw) { Value = created.EventId.ToByteArray() })).Should().Be(1);
        Convert.ToInt32(await fixture.ScalarAsync(
            "SELECT COUNT(*) FROM USER_REF WHERE USER_ID = :usr",
            new OracleParameter("usr", OracleDbType.Raw) { Value = created.UserId.ToByteArray() })).Should().Be(1);
    }
}
