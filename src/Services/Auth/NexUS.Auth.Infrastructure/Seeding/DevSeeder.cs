using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexUS.Auth.Application.Abstractions;
using NexUS.Auth.Domain.Credentials;
using NexUS.Auth.Infrastructure.Persistence;

namespace NexUS.Auth.Infrastructure.Seeding;

public sealed class DevSeedOptions
{
    public const string SectionName = "Seed";

    public long TenantId { get; set; } = 1; // tenant "demo"; tabel tenant milik User Service (NX-018)
    public Guid AdminUserId { get; set; } = Guid.Parse("6f1c2a5e-0d3b-4c8e-9a71-2b4d5e6f7a80");
    public string AdminEmail { get; set; } = "admin@demo.local";
    public string AdminRoles { get; set; } = "admin";

    /// <summary>Password admin dev dari environment/user-secrets; seed dilewati bila kosong.</summary>
    public string? AdminPassword { get; set; }
}

/// <summary>Membuat tenant demo dan user admin@demo.local di Development. Tidak pernah berjalan di environment lain.</summary>
public sealed class DevSeeder(
    IServiceScopeFactory scopes,
    IOptions<DevSeedOptions> options,
    IHostEnvironment environment,
    ILogger<DevSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment()) return;

        var seed = options.Value;
        if (string.IsNullOrWhiteSpace(seed.AdminPassword))
        {
            logger.LogWarning("Seed:AdminPassword kosong; user admin dev tidak dibuat");
            return;
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var email = Credential.NormalizeEmail(seed.AdminEmail);
            if (await db.Credentials.AnyAsync(c => c.Email == email, cancellationToken)) return;

            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var credential = Credential.Create(seed.TenantId, seed.AdminUserId, email, hasher.Hash(seed.AdminPassword));
            db.Credentials.Add(credential);
            db.Entry(credential).Property(AuthDbContext.RolesProperty).CurrentValue = seed.AdminRoles;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("User admin dev dibuat untuk tenant {TenantId}", seed.TenantId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Database belum termigrasi atau belum siap: aplikasi tetap jalan, seed bisa diulang dengan restart.
            logger.LogWarning(ex, "Seed data dev gagal dijalankan");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
