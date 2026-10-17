using NexUS.Auth.Domain.Credentials;

namespace NexUS.Auth.Application.Abstractions;

public interface ICredentialRepository
{
    /// <summary>
    /// Mencari kredensial berdasarkan email yang sudah dinormalisasi. Satu-satunya query lintas tenant
    /// (tenant baru diketahui dari hasilnya), lihat ADR-0006.
    /// </summary>
    Task<Credential?> FindByEmailAsync(string normalizedEmail, CancellationToken ct);

    Task AddAsync(Credential credential, CancellationToken ct);
}

public interface IRefreshTokenRepository
{
    Task AddAsync(RefreshToken token, CancellationToken ct);
}

public interface ILoginAttemptRepository
{
    Task AddAsync(LoginAttempt attempt, CancellationToken ct);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string passwordHash);

    /// <summary>Menjalankan verifikasi palsu dengan biaya yang sama agar waktu respons tidak membocorkan email yang tidak terdaftar.</summary>
    void VerifyDummy(string password);
}

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

public interface IAccessTokenIssuer
{
    AccessToken Issue(Credential credential, IReadOnlyCollection<string> roles);
}

/// <summary>Sumber role user. Sementara dari data Auth sendiri; diganti panggilan ke User Service di NX-020 (ADR-0006).</summary>
public interface IUserDirectory
{
    Task<IReadOnlyCollection<string>> GetRolesAsync(Credential credential, CancellationToken ct);
}
