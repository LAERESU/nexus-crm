using NexUS.Auth.Application.Abstractions;

namespace NexUS.Auth.Infrastructure.Security;

/// <summary>
/// BCrypt "enhanced" (pre-hash SHA-384) agar password lebih dari 72 byte tidak terpotong diam-diam.
/// </summary>
public sealed class BcryptPasswordHasher : IPasswordHasher
{
    public const int WorkFactor = 12;

    // Hash sekali pakai untuk VerifyDummy: biaya verifikasinya sama dengan hash asli.
    private static readonly Lazy<string> DummyHash = new(() => BCrypt.Net.BCrypt.EnhancedHashPassword(Guid.NewGuid().ToString(), WorkFactor));

    public string Hash(string password) => BCrypt.Net.BCrypt.EnhancedHashPassword(password, WorkFactor);

    public bool Verify(string password, string passwordHash)
    {
        try
        {
            return BCrypt.Net.BCrypt.EnhancedVerify(password, passwordHash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false; // hash rusak dianggap password salah, bukan error 500
        }
    }

    public void VerifyDummy(string password) => BCrypt.Net.BCrypt.EnhancedVerify(password, DummyHash.Value);
}
