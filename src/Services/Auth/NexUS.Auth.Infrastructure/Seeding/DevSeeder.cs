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

/// <summary>
/// Membuat user admin@demo.local di Development. Berjalan di latar belakang dengan retry agar tidak menahan
/// startup (dan /health/live) saat Oracle belum siap. Tidak pernah berjalan di environment lain.
/// </summary>
public sealed class DevSeeder(
    IServiceScopeFactory scopes,
    IOptions<DevSeedOptions> options,
    IHostEnvironment environment,
    ILogger<DevSeeder> logger) : BackgroundService
{
    public const int MaxAttempts = 10;
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    /// <summary>Selesai saat seed berhasil, dilewati, atau menyerah (untuk tes).</summary>
    public Task Completion => _completion.Task;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await SeedWithRetryAsync(stoppingToken);
        }
        finally
        {
            _completion.TrySetResult();
        }
    }

    private async Task SeedWithRetryAsync(CancellationToken ct)
    {
        if (!environment.IsDevelopment()) return;

        var seed = options.Value;
        if (string.IsNullOrWhiteSpace(seed.AdminPassword))
        {
            logger.LogWarning("Seed:AdminPassword kosong; user admin dev tidak dibuat");
            return;
        }

        for (var attempt = 1; attempt <= MaxAttempts && !ct.IsCancellationRequested; attempt++)
        {
            try
            {
                await SeedAsync(seed, ct);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Seed data dev gagal (percobaan {Attempt}/{MaxAttempts})", attempt, MaxAttempts);
                await Task.Delay(RetryDelay, ct);
            }
        }
    }

    private async Task SeedAsync(DevSeedOptions seed, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var email = Credential.NormalizeEmail(seed.AdminEmail);
        if (await db.Credentials.AnyAsync(c => c.TenantId == seed.TenantId && c.Email == email, ct)) return;

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var credential = Credential.Create(seed.TenantId, seed.AdminUserId, email, hasher.Hash(seed.AdminPassword!));
        db.Credentials.Add(credential);
        db.Entry(credential).Property(AuthDbContext.RolesProperty).CurrentValue = seed.AdminRoles;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("User admin dev dibuat untuk tenant {TenantId}", seed.TenantId);
    }
}
