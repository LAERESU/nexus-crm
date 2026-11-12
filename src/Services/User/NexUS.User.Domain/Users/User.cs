using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.User.Domain.Roles;

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
    public const int MaxRoleAssignments = 100;

    private readonly List<UserRole> _roles = [];

    public string Email { get; private set; } = default!;
    public string FullName { get; private set; } = default!;
    public string? JobTitle { get; private set; }
    public UserStatus Status { get; private set; } = UserStatus.Active;

    /// <summary>ID internal departemen; null bila belum ditempatkan.</summary>
    public long? DepartmentId { get; private set; }

    public IReadOnlyCollection<UserRole> Roles => _roles;

    private User() { } // untuk EF Core

    /// <param name="publicId">Hanya untuk menautkan ke ID yang sudah ada (seed dev); biasanya dibiarkan kosong.</param>
    public static User Create(
        long tenantId, string email, string fullName, string? jobTitle, long? departmentId, Guid? publicId = null)
    {
        if (tenantId <= 0) throw new DomainException("Tenant wajib diisi.");

        var user = new User { TenantId = tenantId, Email = NormalizeEmail(email) };
        if (publicId is { } id && id != Guid.Empty) user.PublicId = id;
        user.ApplyProfile(fullName, jobTitle);
        user.DepartmentId = departmentId;
        user.AddDomainEvent(new UserCreatedDomainEvent(user.PublicId, tenantId, user.Email, user.FullName));
        return user;
    }

    public void UpdateProfile(string fullName, string? jobTitle, long? departmentId)
    {
        var (oldName, oldTitle, oldDepartment) = (FullName, JobTitle, DepartmentId);

        ApplyProfile(fullName, jobTitle);
        DepartmentId = departmentId;

        // Tanpa perubahan tidak ada UPDATE di database, jadi tidak boleh ada event ke consumer.
        if (FullName != oldName || JobTitle != oldTitle || DepartmentId != oldDepartment)
            AddDomainEvent(new UserUpdatedDomainEvent(PublicId, TenantId, Email, FullName));
    }

    public void Activate()
    {
        if (Status == UserStatus.Active) return;
        Status = UserStatus.Active;
        // User nonaktif tidak punya permission, jadi cache permission-nya harus dihapus saat status berubah.
        AddDomainEvent(new NexUS.User.Domain.Roles.RoleChangedDomainEvent(TenantId, null, PublicId, NexUS.User.Domain.Roles.RoleChangeReason.UserStatusChanged));
        AddDomainEvent(new UserUpdatedDomainEvent(PublicId, TenantId, Email, FullName));
    }

    public void Deactivate()
    {
        if (Status == UserStatus.Inactive) return;
        Status = UserStatus.Inactive;
        AddDomainEvent(new NexUS.User.Domain.Roles.RoleChangedDomainEvent(TenantId, null, PublicId, NexUS.User.Domain.Roles.RoleChangeReason.UserStatusChanged));
        AddDomainEvent(new UserDeactivatedDomainEvent(PublicId, TenantId));
    }

    /// <summary>
    /// Mengganti seluruh penugasan role user. Penugasan ganda diabaikan; event <see cref="RoleChangedDomainEvent"/>
    /// hanya bila himpunan penugasan benar-benar berubah. Keberadaan role dan scope diperiksa di Application layer.
    /// </summary>
    public void SetRoles(IReadOnlyCollection<RoleAssignment> desired)
    {
        var assignments = desired.Select(a => a.Validated()).Distinct().ToList();
        if (assignments.Count > MaxRoleAssignments)
            throw new DomainException($"Satu user maksimal memiliki {MaxRoleAssignments} penugasan role.");

        var removed = _roles.Where(existing => !assignments.Any(existing.Matches)).ToList();
        var added = assignments.Where(a => !_roles.Any(existing => existing.Matches(a))).ToList();
        if (removed.Count == 0 && added.Count == 0) return;

        foreach (var role in removed) _roles.Remove(role);
        foreach (var assignment in added) _roles.Add(UserRole.Create(TenantId, assignment));

        AddDomainEvent(new RoleChangedDomainEvent(TenantId, RoleId: null, PublicId, RoleChangeReason.UserRolesChanged));
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
