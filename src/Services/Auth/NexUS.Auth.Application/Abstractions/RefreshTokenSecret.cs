using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace NexUS.Auth.Application.Abstractions;

/// <summary>
/// Refresh token untuk klien berformat <c>{tenantId}.{256 bit acak}</c>; yang disimpan hanya hash SHA-256 dari
/// seluruh nilai. Awalan tenant membuat pencarian token tetap memfilter TENANT_ID (CONTEXT.md bagian 3 poin 8).
/// </summary>
public static class RefreshTokenSecret
{
    public static string Generate(long tenantId) =>
        $"{tenantId.ToString(CultureInfo.InvariantCulture)}.{Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32))}";

    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>Membaca tenant dari token; false bila format tidak valid.</summary>
    public static bool TryGetTenantId(string? token, out long tenantId)
    {
        tenantId = 0;
        if (string.IsNullOrEmpty(token) || token.Length > 128) return false;

        var separator = token.IndexOf('.');
        return separator > 0
               && separator < token.Length - 1
               && long.TryParse(token.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out tenantId)
               && tenantId > 0;
    }
}
