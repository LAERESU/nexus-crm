using MediatR;
using Microsoft.Extensions.Logging;
using NexUS.Auth.Application.Abstractions;
using NexUS.Auth.Application.Common;
using NexUS.Auth.Domain.Credentials;

namespace NexUS.Auth.Application.Invitations;

/// <summary>Dijalankan saat event <c>UserCreated</c> diterima: membuat kredensial tanpa password dan mengirim undangan.</summary>
public sealed record InviteUserCommand(Guid EventId, long TenantId, Guid UserId, string Email, string FullName) : IRequest;

public sealed class InviteUserCommandHandler(
    ICredentialRepository credentials,
    IProcessedEventStore processedEvents,
    IInvitationEmailSender emailSender,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<InviteUserCommandHandler> logger) : IRequestHandler<InviteUserCommand>
{
    public const string Consumer = "auth.UserCreated";

    public Task Handle(InviteUserCommand command, CancellationToken ct) =>
        // Satu transaksi: kredensial, catatan event, dan (lewat rollback) kegagalan kirim email. Bila email gagal,
        // tidak ada yang tersimpan dan event dikirim ulang oleh broker.
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            if (await processedEvents.IsProcessedAsync(command.EventId, Consumer, token)) return;

            var now = timeProvider.GetUtcNow();
            await processedEvents.MarkAsync(command.EventId, Consumer, now, token);

            if (!System.Net.Mail.MailAddress.TryCreate(command.Email?.Trim(), out _))
            {
                // Mengulang tidak akan membantu (alamat tidak bisa dikirimi email), jadi event dianggap selesai.
                logger.LogError("Undangan user {UserId} di tenant {TenantId} dilewati: alamat email tidak valid",
                    command.UserId, command.TenantId);
                await unitOfWork.SaveChangesAsync(token);
                return;
            }

            var email = Credential.NormalizeEmail(command.Email!);
            if (await credentials.FindByUserAsync(command.TenantId, command.UserId, token) is not null)
            {
                // Sudah punya kredensial (seed dev, atau event lama yang dikirim ulang dengan ID berbeda).
                await unitOfWork.SaveChangesAsync(token);
                return;
            }

            if (await credentials.FindByEmailAsync(email, token) is not null)
            {
                // Email login unik global (ADR-0006); mengulang tidak akan membantu, jadi event dianggap selesai.
                logger.LogWarning("Undangan user {UserId} di tenant {TenantId} dilewati: email sudah dipakai kredensial lain",
                    command.UserId, command.TenantId);
                await unitOfWork.SaveChangesAsync(token);
                return;
            }

            var activationToken = ActivationToken.Generate(command.TenantId);
            var credential = Credential.Invite(command.TenantId, command.UserId, email, ActivationToken.Hash(activationToken), now);
            await credentials.AddAsync(credential, token);
            await unitOfWork.SaveChangesAsync(token);

            await emailSender.SendAsync(
                new InvitationEmail(email, command.FullName, activationToken, credential.ActivationExpiresAt!.Value), token);
            logger.LogInformation("Undangan aktivasi dikirim untuk user {UserId} di tenant {TenantId}", command.UserId, command.TenantId);
        }, ct);
}
