namespace NexUS.BuildingBlocks.Common.Domain;

/// <summary>
/// User yang sedang login, dari klaim access token. <see cref="UserId"/> adalah ID publik user (klaim <c>sub</c>),
/// karena ID internal tidak diekspos ke luar service (CONTEXT.md bagian 5). Permission tidak ada di token dan
/// diambil dari User Service dengan cache (NX-020).
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>ID publik user; <see cref="Guid.Empty"/> bila belum login.</summary>
    Guid UserId { get; }

    /// <summary>Tenant user; 0 bila belum login.</summary>
    long TenantId { get; }

    /// <summary>ID sesi (keluarga refresh token); null bila tidak ada.</summary>
    string? SessionId { get; }

    IReadOnlyCollection<string> Roles { get; }
}
