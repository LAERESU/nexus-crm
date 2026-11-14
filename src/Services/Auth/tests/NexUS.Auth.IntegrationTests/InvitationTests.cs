using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexUS.BuildingBlocks.Messaging;
using NexUS.BuildingBlocks.Messaging.Contracts;
using Oracle.ManagedDataAccess.Client;

namespace NexUS.Auth.IntegrationTests;

/// <summary>
/// Alur undangan dari ujung ke ujung di sisi Auth: event <c>UserCreated</c> lewat RabbitMQ sungguhan, email lewat SMTP
/// ke Mailpit, aktivasi lewat HTTP, lalu login.
/// </summary>
[Collection(OracleCollection.Name)]
[Trait("Category", "Oracle")]
public partial class InvitationTests(OracleAuthFixture fixture)
{
    private const string Password = "Rahasia#Baru2026";
    private const long Tenant = 1;

    private sealed record MailAddress(string Address);

    private sealed record MailSummary(string ID, List<MailAddress> To, string Subject);

    private sealed record MailList(List<MailSummary> Messages);

    private sealed record MailDetail(string Text);

    [GeneratedRegex(@"token=(\S+)")]
    private static partial Regex TokenPattern();

    private HttpClient Client() => fixture.Factory.CreateClient(new() { HandleCookies = false });

    private static string NewEmail() => $"undangan-{Guid.NewGuid():N}@contoh.com";

    private async Task<UserCreated> PublishUserCreatedAsync(string email, Guid? eventId = null)
    {
        var created = new UserCreated(eventId ?? Guid.NewGuid(), DateTimeOffset.UtcNow, Tenant, Guid.NewGuid(), email, "Pengguna Baru");
        await PublishAsync(created);
        return created;
    }

    private async Task PublishAsync(UserCreated created)
    {
        await fixture.WaitForQueueAsync("auth.UserCreated");
        await fixture.Factory.Services.GetRequiredService<IEventPublisher>().PublishAsync(created);
    }

    private async Task<List<MailSummary>> MailsToAsync(string email)
    {
        var list = await fixture.Mailpit.GetFromJsonAsync<MailList>("api/v1/messages?limit=200");
        return list!.Messages.Where(m => m.To.Any(t => string.Equals(t.Address, email, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    /// <summary>Menunggu email undangan tiba lalu mengambil token aktivasi dari tautannya.</summary>
    private async Task<string> WaitForTokenAsync(string email)
    {
        for (var attempt = 0; attempt < 120; attempt++)
        {
            if ((await MailsToAsync(email)).FirstOrDefault() is { } mail)
            {
                var detail = await fixture.Mailpit.GetFromJsonAsync<MailDetail>($"api/v1/message/{mail.ID}");
                return Uri.UnescapeDataString(TokenPattern().Match(detail!.Text).Groups[1].Value);
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"Email undangan untuk {email} tidak tiba.");
    }

    private Task<HttpResponseMessage> ActivateAsync(string token, string password = Password) =>
        Client().PostAsJsonAsync("/api/v1/auth/activate", new { token, password });

    private Task<HttpResponseMessage> LoginAsync(string email, string password = Password) =>
        Client().PostAsJsonAsync("/api/v1/auth/login", new { email, password });

    private async Task<int> ExecuteAsync(string sql, params OracleParameter[] parameters)
    {
        await using var connection = new OracleConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.BindByName = true;
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        return Convert.ToInt32(sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            ? await command.ExecuteScalarAsync()
            : await command.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task User_baru_menerima_email_dan_bisa_login_setelah_set_password()
    {
        var email = NewEmail();
        await PublishUserCreatedAsync(email);
        var token = await WaitForTokenAsync(email);

        (await LoginAsync(email)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "akun belum diaktifkan");
        token.Should().StartWith($"{Tenant}.");

        (await ActivateAsync(token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await LoginAsync(email)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoginAsync(email, "Password#Salah1")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_aktivasi_hanya_bisa_dipakai_sekali_dan_tidak_disimpan_apa_adanya()
    {
        var email = NewEmail();
        await PublishUserCreatedAsync(email);
        var token = await WaitForTokenAsync(email);

        (await ExecuteAsync("SELECT COUNT(*) FROM CREDENTIALS WHERE ACTIVATION_TOKEN_HASH = :t", new OracleParameter("t", token)))
            .Should().Be(0, "yang disimpan hanya hash");

        (await ActivateAsync(token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ActivateAsync(token, "Password#Lain2026")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await LoginAsync(email)).StatusCode.Should().Be(HttpStatusCode.OK, "password pertama tetap berlaku");
    }

    [Fact]
    public async Task Token_aktivasi_kedaluwarsa_tidak_bisa_dipakai()
    {
        var email = NewEmail();
        await PublishUserCreatedAsync(email);
        var token = await WaitForTokenAsync(email);

        // Mundurkan masa berlaku melewati 72 jam.
        (await ExecuteAsync(
            "UPDATE CREDENTIALS SET ACTIVATION_EXPIRES_AT = SYSTIMESTAMP - INTERVAL '1' MINUTE WHERE EMAIL = :e",
            new OracleParameter("e", email))).Should().Be(1);

        (await ActivateAsync(token)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await LoginAsync(email)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_salah_dan_password_lemah_ditolak()
    {
        (await ActivateAsync($"{Tenant}.token-yang-tidak-ada")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ActivateAsync("tanpa-format")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ActivateAsync($"{Tenant}.apa-saja", "pendek")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Event_yang_sama_dikirim_dua_kali_hanya_menghasilkan_satu_kredensial_dan_satu_email()
    {
        var email = NewEmail();
        var first = await PublishUserCreatedAsync(email);
        await WaitForTokenAsync(email);

        await PublishAsync(first);
        // Penanda: event berikutnya selesai diproses berarti duplikat di depannya juga sudah lewat antrean.
        var marker = NewEmail();
        await PublishUserCreatedAsync(marker);
        await WaitForTokenAsync(marker);

        (await MailsToAsync(email)).Should().ContainSingle();
        (await ExecuteAsync("SELECT COUNT(*) FROM CREDENTIALS WHERE EMAIL = :e", new OracleParameter("e", email))).Should().Be(1);
        (await ExecuteAsync(
            "SELECT COUNT(*) FROM PROCESSED_EVENTS WHERE EVENT_ID = :id",
            new OracleParameter("id", OracleDbType.Raw) { Value = first.EventId.ToByteArray() })).Should().Be(1);
    }
}
