using MediatR;

namespace NexUS.Auth.Application.Logout;

/// <summary>
/// Logout sesi saat ini. Refresh token (cookie) mengidentifikasi sesi yang dicabut; access token (Bearer)
/// dimasukkan ke blacklist sampai kedaluwarsa. Keduanya opsional agar logout selalu berhasil di sisi klien.
/// </summary>
public sealed record LogoutCommand(string? RefreshToken, string? AccessToken) : IRequest;
