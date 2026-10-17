using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace NexUS.Auth.Application.Abstractions;

/// <summary>Refresh token acak 256 bit untuk klien; yang disimpan hanya hash SHA-256-nya.</summary>
public static class RefreshTokenSecret
{
    public static string Generate() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
