using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;

namespace NexUS.User.Domain.Users;

public enum UserStatus
{
    Active,
    Inactive,
}

/// <summary>Profil user dalam satu tenant. Kredensial login dikelola Auth Service.</summary>
public sealed class User : AggregateRoot
{
    public const int EmailMaxLength = 320;
    public const int FullNameMaxLength = 200;
    public const int JobTitleMaxLength = 200;

    public string Email { get; private set; } = default!;
    public string FullName { get; private set; } = default!;
    public string? JobTitle { get; private set; }
    public UserStatus Status { get; private set; } = UserStatus.Active;

    /// <summary>ID internal departemen; null bila belum ditempatkan.</summary>
    public long? DepartmentId { get; private set; }

    private User() { } // untuk EF Core

    public static User Create(long tenantId, string email, string fullName, string? jobTitle, long? departmentId)
    {
        if (tenantId <= 0) throw new DomainException("Tenant wajib diisi.");

        var user = new User { TenantId = tenantId, Email = NormalizeEmail(email) };
        user.ApplyProfile(fullName, jobTitle);
        user.DepartmentId = departmentId;
        user.AddDomainEvent(new UserCreatedDomainEvent(user.PublicId, tenantId, user.Email, user.FullName));
        return user;
    }

    public void UpdateProfile(string fullName, string? jobTitle, long? departmentId)
    {
        ApplyProfile(fullName, jobTitle);
        DepartmentId = departmentId;
        AddDomainEvent(new UserUpdatedDomainEvent(PublicId, TenantId, Email, FullName));
    }

    public void Activate()
    {
        if (Status == UserStatus.Active) return;
        Status = UserStatus.Active;
        AddDomainEvent(new UserUpdatedDomainEvent(PublicId, TenantId, Email, FullName));
    }

    public void Deactivate()
    {
        if (Status == UserStatus.Inactive) return;
        Status = UserStatus.Inactive;
        AddDomainEvent(new UserDeactivatedDomainEvent(PublicId, TenantId));
    }

    public static string NormalizeEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) throw new DomainException("Email wajib diisi.");
        var normalized = email.Trim().ToLowerInvariant();
        if (normalized.Length > EmailMaxLength) throw new DomainException($"Email maksimal {EmailMaxLength} karakter.");
        return normalized;
    }

    private void ApplyProfile(string fullName, string? jobTitle)
    {
        if (string.IsNullOrWhiteSpace(fullName)) throw new DomainException("Nama lengkap wajib diisi.");
        if (fullName.Trim().Length > FullNameMaxLength) throw new DomainException($"Nama lengkap maksimal {FullNameMaxLength} karakter.");
        if (jobTitle is { Length: > JobTitleMaxLength }) throw new DomainException($"Jabatan maksimal {JobTitleMaxLength} karakter.");

        FullName = fullName.Trim();
        JobTitle = string.IsNullOrWhiteSpace(jobTitle) ? null : jobTitle.Trim();
    }
}

public sealed record UserCreatedDomainEvent(Guid UserId, long TenantId, string Email, string FullName) : DomainEvent;

public sealed record UserUpdatedDomainEvent(Guid UserId, long TenantId, string Email, string FullName) : DomainEvent;

public sealed record UserDeactivatedDomainEvent(Guid UserId, long TenantId) : DomainEvent;
