using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using NexUS.Auth.Application.Abstractions;
using NexUS.Auth.Application.Common;
using NexUS.BuildingBlocks.Common.Exceptions;

namespace NexUS.Auth.Application.Invitations;

/// <summary>Mengisi password pertama dengan token aktivasi dari email undangan.</summary>
public sealed record ActivateAccountCommand(string Token, string Password) : IRequest;

public sealed class ActivateAccountCommandValidator : AbstractValidator<ActivateAccountCommand>
{
    public const int MinPasswordLength = 10;

    public ActivateAccountCommandValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(MinPasswordLength).MaximumLength(200);
    }
}

public sealed class ActivateAccountCommandHandler(
    ICredentialRepository credentials,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<ActivateAccountCommandHandler> logger) : IRequestHandler<ActivateAccountCommand>
{
    /// <summary>Satu pesan untuk token salah, kedaluwarsa, maupun sudah dipakai.</summary>
    public const string FailureMessage = "Token aktivasi tidak valid atau sudah kedaluwarsa.";

    public async Task Handle(ActivateAccountCommand command, CancellationToken ct)
    {
        if (!ActivationToken.TryGetTenantId(command.Token, out var tenantId)) throw new DomainException(FailureMessage);

        var now = timeProvider.GetUtcNow();
        var credential = await credentials.FindByActivationHashAsync(tenantId, ActivationToken.Hash(command.Token), ct);
        if (credential is null || !credential.CanActivate(now)) throw new DomainException(FailureMessage);

        credential.Activate(passwordHasher.Hash(command.Password), now);
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (ConcurrencyException)
        {
            throw new DomainException(FailureMessage); // token yang sama dipakai dua request sekaligus: hanya satu yang menang
        }

        logger.LogInformation("Akun user {UserId} di tenant {TenantId} diaktifkan", credential.UserId, credential.TenantId);
    }
}
