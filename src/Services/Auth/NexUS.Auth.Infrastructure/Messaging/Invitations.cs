using System.Net;
using System.Net.Mail;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexUS.Auth.Application.Abstractions;
using NexUS.Auth.Application.Invitations;
using NexUS.Auth.Infrastructure.Persistence;
using NexUS.BuildingBlocks.Messaging;
using NexUS.BuildingBlocks.Messaging.Contracts;

namespace NexUS.Auth.Infrastructure.Messaging;

/// <summary>Consumer <c>UserCreated</c>: meneruskan ke use case undangan (idempotent di sana).</summary>
public sealed class UserCreatedHandler(ISender mediator) : IIntegrationEventHandler<UserCreated>
{
    public Task HandleAsync(UserCreated integrationEvent, CancellationToken ct) =>
        mediator.Send(new InviteUserCommand(
            integrationEvent.EventId, integrationEvent.TenantId, integrationEvent.UserId, integrationEvent.Email, integrationEvent.FullName), ct);
}

public sealed class ProcessedEventStore(AuthDbContext db) : IProcessedEventStore
{
    public Task<bool> IsProcessedAsync(Guid eventId, string consumer, CancellationToken ct) =>
        db.ProcessedEvents.AnyAsync(e => e.EventId == eventId && e.Consumer == consumer, ct);

    public async Task MarkAsync(Guid eventId, string consumer, DateTimeOffset now, CancellationToken ct) =>
        await db.ProcessedEvents.AddAsync(ProcessedEvent.Create(eventId, consumer, now), ct);
}

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>Host SMTP (Mailpit di Development). Kosong berarti email tidak dapat dikirim.</summary>
    public string? SmtpHost { get; set; }

    public int SmtpPort { get; set; } = 1025;
    public bool EnableSsl { get; set; }
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string From { get; set; } = "no-reply@nexus.local";

    /// <summary>Alamat halaman aktivasi di aplikasi web; <c>{token}</c> diganti token aktivasi.</summary>
    public string ActivationUrlTemplate { get; set; } = "http://localhost:5173/activate?token={token}";
}

/// <summary>Email undangan langsung lewat SMTP (sementara sampai Notification Service ada, NX-057).</summary>
public sealed class SmtpInvitationEmailSender(IOptions<EmailOptions> options) : IInvitationEmailSender
{
    public async Task SendAsync(InvitationEmail invitation, CancellationToken ct)
    {
        var email = options.Value;
        if (string.IsNullOrWhiteSpace(email.SmtpHost))
            throw new InvalidOperationException("Email:SmtpHost belum dikonfigurasi; undangan tidak dapat dikirim.");

        var url = email.ActivationUrlTemplate.Replace("{token}", Uri.EscapeDataString(invitation.ActivationToken), StringComparison.Ordinal);
        using var message = new MailMessage(email.From, invitation.Email)
        {
            Subject = "Undangan bergabung di NexUS",
            Body =
                $"Halo {invitation.FullName},\n\n" +
                "Akun NexUS Anda sudah dibuat. Buka tautan berikut untuk membuat password dan mengaktifkan akun:\n\n" +
                $"{url}\n\n" +
                $"Tautan berlaku sampai {invitation.ExpiresAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC dan hanya dapat dipakai satu kali.\n" +
                "Abaikan email ini bila Anda tidak merasa didaftarkan.\n",
        };

        using var client = new SmtpClient(email.SmtpHost, email.SmtpPort) { EnableSsl = email.EnableSsl };
        if (!string.IsNullOrEmpty(email.UserName)) client.Credentials = new NetworkCredential(email.UserName, email.Password);
        await client.SendMailAsync(message, ct);
    }
}
