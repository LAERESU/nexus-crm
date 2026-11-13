using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using NexUS.BuildingBlocks.Messaging;
using NexUS.BuildingBlocks.Messaging.Contracts;
using RabbitMQ.Client;

namespace NexUS.User.IntegrationTests;

[Collection(OracleCollection.Name)]
[Trait("Category", "Oracle")]
public class OutboxRelayTests(OracleUserFixture fixture)
{
    private sealed record UserBody(Guid Id, string Email);

    [Fact]
    public async Task UserCreated_dikirim_ke_RabbitMQ_dan_outbox_ditandai_terproses()
    {
        // Antrean uji diikat sebelum user dibuat, seperti antrean consumer Auth Service.
        var factory = new ConnectionFactory { Uri = new Uri(fixture.RabbitConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await EventBusTopology.DeclareExchangeAsync(channel, default);
        var queue = (await channel.QueueDeclareAsync(queue: string.Empty, durable: false, exclusive: true, autoDelete: true)).QueueName;
        await channel.QueueBindAsync(queue, EventBusTopology.Exchange, nameof(UserCreated));

        var email = $"relay-{Guid.NewGuid():N}@contoh.com";
        var response = await fixture.ClientAs(OracleUserFixture.TenantA)
            .PostAsJsonAsync("/api/v1/users", new { email, fullName = "Pengguna Relay" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var user = (await response.Content.ReadFromJsonAsync<UserBody>())!;

        UserCreated? received = null;
        string? messageId = null;
        for (var attempt = 0; attempt < 120 && received is null; attempt++)
        {
            while (await channel.BasicGetAsync(queue, autoAck: true) is { } message)
            {
                var candidate = JsonSerializer.Deserialize<UserCreated>(Encoding.UTF8.GetString(message.Body.Span), EventBusTopology.Json)!;
                if (candidate.Email != email) continue; // event dari tes lain di koleksi yang sama

                received = candidate;
                messageId = message.BasicProperties.MessageId;
            }

            if (received is null) await Task.Delay(250);
        }

        received.Should().NotBeNull("relay mengirim UserCreated dalam beberapa detik");
        received!.UserId.Should().Be(user.Id);
        received.TenantId.Should().Be(OracleUserFixture.TenantA);
        received.FullName.Should().Be("Pengguna Relay");
        messageId.Should().Be(received.EventId.ToString("D"));

        // Event tanpa kontrak (mis. RoleChanged dari seed) tidak dibuang: tetap tertunda di outbox.
        Convert.ToInt32(await fixture.ScalarAsync(
            "SELECT COUNT(*) FROM OUTBOX_MESSAGES WHERE TYPE LIKE '%RoleChangedDomainEvent%' AND PROCESSED_AT IS NOT NULL")).Should().Be(0);
        Convert.ToInt32(await fixture.ScalarAsync(
            "SELECT COUNT(*) FROM OUTBOX_MESSAGES WHERE TYPE LIKE '%RoleChangedDomainEvent%' AND PROCESSED_AT IS NULL")).Should().BeGreaterThan(0);

        // ID event = ID baris outbox, dan baris itu ditandai terproses (tidak dikirim ulang).
        var processed = 0;
        for (var attempt = 0; attempt < 40 && processed == 0; attempt++)
        {
            processed = Convert.ToInt32(await fixture.ScalarAsync(
                $"SELECT COUNT(*) FROM OUTBOX_MESSAGES WHERE PAYLOAD LIKE '%{email}%' AND PROCESSED_AT IS NOT NULL"));
            if (processed == 0) await Task.Delay(250);
        }

        processed.Should().Be(1);
    }
}
