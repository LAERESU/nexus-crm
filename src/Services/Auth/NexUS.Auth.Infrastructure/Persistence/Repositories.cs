using Microsoft.EntityFrameworkCore;
using NexUS.Auth.Application.Abstractions;
using NexUS.Auth.Domain.Credentials;

namespace NexUS.Auth.Infrastructure.Persistence;

public sealed class CredentialRepository(AuthDbContext db) : ICredentialRepository
{
    // Pengecualian filter TENANT_ID yang tercatat di ADR-0006: tenant baru diketahui dari hasil query ini.
    public Task<Credential?> FindByEmailAsync(string normalizedEmail, CancellationToken ct) =>
        db.Credentials.FirstOrDefaultAsync(c => c.Email == normalizedEmail, ct);

    public async Task AddAsync(Credential credential, CancellationToken ct) =>
        await db.Credentials.AddAsync(credential, ct);

    public Task ReloadAsync(Credential credential, CancellationToken ct) => db.Entry(credential).ReloadAsync(ct);
}

public sealed class RefreshTokenRepository(AuthDbContext db) : IRefreshTokenRepository
{
    public async Task AddAsync(RefreshToken token, CancellationToken ct) =>
        await db.RefreshTokens.AddAsync(token, ct);
}

public sealed class LoginAttemptRepository(AuthDbContext db) : ILoginAttemptRepository
{
    public async Task AddAsync(LoginAttempt attempt, CancellationToken ct) =>
        await db.LoginAttempts.AddAsync(attempt, ct);
}

/// <summary>Implementasi sementara <see cref="IUserDirectory"/>: role dari kolom CREDENTIALS.ROLES (ADR-0006).</summary>
public sealed class CredentialRolesUserDirectory(AuthDbContext db) : IUserDirectory
{
    public async Task<IReadOnlyCollection<string>> GetRolesAsync(Credential credential, CancellationToken ct)
    {
        // Kredensial biasanya sudah dilacak DbContext yang sama (dimuat saat login), jadi tidak perlu query kedua.
        var entry = db.Entry(credential);
        if (entry.State == EntityState.Detached)
        {
            entry = db.Attach(credential);
            await entry.ReloadAsync(ct);
        }

        var roles = entry.Property<string?>(AuthDbContext.RolesProperty).CurrentValue;
        return string.IsNullOrWhiteSpace(roles)
            ? []
            : roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
