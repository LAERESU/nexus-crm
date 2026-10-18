using MediatR;
using NexUS.Auth.Application.Login;
using NexUS.BuildingBlocks.Common.Exceptions;

namespace NexUS.Auth.Application.Refresh;

public sealed record RefreshTokenCommand(string? RefreshToken, string? IpAddress, string? UserAgent) : IRequest<LoginResult>;

/// <summary>
/// Refresh ditolak (401). <see cref="ClearCookie"/> false bila cookie milik klien mungkin sudah diganti oleh
/// request lain yang menang (refresh bersamaan), sehingga cookie baru itu tidak boleh dihapus.
/// </summary>
public sealed class RefreshRejectedException(string message, bool clearCookie) : UnauthorizedException(message)
{
    public bool ClearCookie { get; } = clearCookie;
}
