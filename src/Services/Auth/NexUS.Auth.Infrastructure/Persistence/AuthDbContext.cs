using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexUS.Auth.Application.Common;
using NexUS.Auth.Domain.Credentials;
using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.BuildingBlocks.Messaging;

namespace NexUS.Auth.Infrastructure.Persistence;

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options, TimeProvider timeProvider)
    : DbContext(options), IUnitOfWork
{
    /// <summary>Pelaku sistem untuk kolom CREATED_BY/UPDATED_BY saat belum ada user yang login.</summary>
    public const long SystemActor = 0;

    /// <summary>Kolom sementara CREDENTIALS.ROLES (ADR-0006), dipetakan sebagai shadow property agar tidak masuk Domain.</summary>
    public const string RolesProperty = "Roles";

    public DbSet<Credential> Credentials => Set<Credential>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<LoginAttempt> LoginAttempts => Set<LoginAttempt>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("AUTH_SCHEMA");

        model.Entity<Credential>(e =>
        {
            e.ToTable("CREDENTIALS");
            MapEntity(e);
            e.Ignore(x => x.DomainEvents);
            e.Property(x => x.UserId).HasColumnName("USER_ID");
            e.Property(x => x.Email).HasColumnName("EMAIL").HasMaxLength(320);
            e.Property(x => x.PasswordHash).HasColumnName("PASSWORD_HASH").HasMaxLength(200);
            e.Property(x => x.FailedAttempts).HasColumnName("FAILED_ATTEMPTS");
            e.Property(x => x.FirstFailedAt).HasColumnName("FIRST_FAILED_AT");
            e.Property(x => x.LockedUntil).HasColumnName("LOCKED_UNTIL");
            e.Property<string?>(RolesProperty).HasColumnName("ROLES").HasMaxLength(1000);
            e.HasIndex(x => x.Email).IsUnique();
        });

        model.Entity<RefreshToken>(e =>
        {
            e.ToTable("REFRESH_TOKENS");
            MapEntity(e);
            e.Property(x => x.CredentialId).HasColumnName("CREDENTIAL_ID");
            e.Property(x => x.TokenHash).HasColumnName("TOKEN_HASH").HasMaxLength(64);
            e.Property(x => x.ExpiresAt).HasColumnName("EXPIRES_AT");
            e.Property(x => x.FamilyId).HasColumnName("FAMILY_ID");
            e.Property(x => x.RevokedAt).HasColumnName("REVOKED_AT");
            e.Property(x => x.RevokedReason).HasColumnName("REVOKED_REASON").HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.ReplacedById).HasColumnName("REPLACED_BY_ID");
            e.HasOne(x => x.ReplacedBy).WithMany().HasForeignKey(x => x.ReplacedById);
            e.HasIndex(x => x.FamilyId);
            e.Property(x => x.DeviceInfo).HasColumnName("DEVICE_INFO").HasMaxLength(500);
            e.Property(x => x.CreatedIp).HasColumnName("CREATED_IP").HasMaxLength(45);
            e.HasOne<Credential>().WithMany().HasForeignKey(x => x.CredentialId);
        });

        model.Entity<LoginAttempt>(e =>
        {
            e.ToTable("LOGIN_HISTORY");
            MapEntity(e);
            e.Property(x => x.CredentialId).HasColumnName("CREDENTIAL_ID");
            e.Property(x => x.Succeeded).HasColumnName("SUCCEEDED").HasColumnType("NUMBER(1)");
            e.Property(x => x.FailureReason).HasColumnName("FAILURE_REASON").HasConversion<string>().HasMaxLength(50);
            e.Property(x => x.IpAddress).HasColumnName("IP_ADDRESS").HasMaxLength(45);
            e.Property(x => x.UserAgent).HasColumnName("USER_AGENT").HasMaxLength(500);
            e.Property(x => x.OccurredAt).HasColumnName("OCCURRED_AT");
            e.HasOne<Credential>().WithMany().HasForeignKey(x => x.CredentialId);
        });

        model.Entity<OutboxMessage>(e =>
        {
            e.ToTable("OUTBOX_MESSAGES");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("ID").ValueGeneratedNever();
            e.Property(x => x.Type).HasColumnName("TYPE").HasMaxLength(500);
            e.Property(x => x.Payload).HasColumnName("PAYLOAD").HasColumnType("CLOB");
            e.Property(x => x.OccurredAt).HasColumnName("OCCURRED_AT");
            e.Property(x => x.ProcessedAt).HasColumnName("PROCESSED_AT");
            e.Property(x => x.RetryCount).HasColumnName("RETRY_COUNT");
            e.Property(x => x.Error).HasColumnName("ERROR").HasMaxLength(2000);
        });
    }

    /// <summary>Kolom wajib CONTEXT.md bagian 5; soft delete difilter otomatis.</summary>
    private static void MapEntity<T>(EntityTypeBuilder<T> e) where T : Entity
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("ID").ValueGeneratedOnAdd();
        e.Property(x => x.PublicId).HasColumnName("PUBLIC_ID");
        e.Property(x => x.TenantId).HasColumnName("TENANT_ID");
        e.Property(x => x.CreatedAt).HasColumnName("CREATED_AT");
        e.Property(x => x.CreatedBy).HasColumnName("CREATED_BY");
        e.Property(x => x.UpdatedAt).HasColumnName("UPDATED_AT");
        e.Property(x => x.UpdatedBy).HasColumnName("UPDATED_BY");
        e.Property(x => x.RowVersion).HasColumnName("ROW_VERSION").IsConcurrencyToken();
        // Tanpa HasConversion<int>: provider Oracle sudah memetakan bool ke NUMBER(1); konversi manual membuat
        // pembuatan literal filter soft delete gagal (InvalidCastException di OracleBoolTypeMapping).
        e.Property(x => x.IsDeleted).HasColumnName("IS_DELETED").HasColumnType("NUMBER(1)");
        e.HasIndex(x => x.PublicId).IsUnique();
        e.HasQueryFilter(x => !x.IsDeleted);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.CreatedBy = SystemActor;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedBy = SystemActor;
                entry.Entity.RowVersion++;
            }
        }

        // Outbox pattern: domain event disimpan dalam transaksi yang sama dengan perubahan data.
        var events = ChangeTracker.Entries<AggregateRoot>().SelectMany(e => e.Entity.PopDomainEvents()).ToList();
        Outbox.AddRange(events.Select(OutboxMessage.From));

        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyException($"Data diubah oleh proses lain: {ex.Entries.Count} entitas bentrok.");
        }
    }
}
