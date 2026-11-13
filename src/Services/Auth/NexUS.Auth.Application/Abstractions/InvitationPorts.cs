namespace NexUS.Auth.Application.Abstractions;

/// <summary>
/// Token aktivasi berformat sama dengan refresh token (<c>{tenantId}.{256 bit acak}</c>) agar pencariannya tetap
/// memfilter TENANT_ID; yang disimpan hanya hash SHA-256.
/// </summary>
public static class ActivationToken
{
    public static string Generate(long tenantId) => RefreshTokenSecret.Generate(tenantId);

    public static string Hash(string token) => RefreshTokenSecret.Hash(token);

    public static bool TryGetTenantId(string? token, out long tenantId) => RefreshTokenSecret.TryGetTenantId(token, out tenantId);
}

/// <param name="ActivationToken">Token asli; hanya boleh keluar lewat email, tidak pernah dicatat di log.</param>
public sealed record InvitationEmail(string Email, string FullName, string ActivationToken, DateTimeOffset ExpiresAt);

/// <summary>Mengirim email undangan. Sementara langsung lewat SMTP; dipindah ke Notification Service di NX-057.</summary>
public interface IInvitationEmailSender
{
    Task SendAsync(InvitationEmail invitation, CancellationToken ct);
}

/// <summary>Catatan event yang sudah diproses (<c>PROCESSED_EVENTS</c>) agar consumer idempotent.</summary>
public interface IProcessedEventStore
{
    Task<bool> IsProcessedAsync(Guid eventId, string consumer, CancellationToken ct);

    /// <summary>Menambahkan catatan ke unit of work yang sedang berjalan; tersimpan bersama perubahan lain.</summary>
    Task MarkAsync(Guid eventId, string consumer, DateTimeOffset now, CancellationToken ct);
}
