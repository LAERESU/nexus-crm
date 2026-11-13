using NexUS.BuildingBlocks.Common.Domain;

namespace NexUS.Project.Domain.Users;

/// <summary>
/// Read model user milik User Service (tabel <c>USER_REF</c>), diperbarui dari event. Event bisa tiba tidak berurutan,
/// jadi perubahan dari event yang lebih lama daripada <see cref="LastEventAt"/> diabaikan.
/// </summary>
public sealed class UserRef : Entity
{
    /// <summary>ID publik user di User Service.</summary>
    public Guid UserId { get; private set; }

    public string? Email { get; private set; }
    public string? FullName { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset LastEventAt { get; private set; }

    private UserRef() { } // untuk EF Core

    /// <summary>Baris baru yang belum memuat event apa pun; isi dengan <see cref="ApplyProfile"/> atau <see cref="Deactivate"/>.</summary>
    public static UserRef Create(long tenantId, Guid userId) =>
        new() { TenantId = tenantId, UserId = userId, IsActive = true, LastEventAt = DateTimeOffset.MinValue };

    /// <summary>Menerapkan profil dari <c>UserCreated</c>/<c>UserUpdated</c>. Mengembalikan false bila event sudah usang.</summary>
    public bool ApplyProfile(string email, string fullName, bool isActive, DateTimeOffset occurredAt)
    {
        if (occurredAt < LastEventAt) return false;

        Email = email;
        FullName = fullName;
        IsActive = isActive;
        LastEventAt = occurredAt;
        return true;
    }

    /// <summary>Menerapkan <c>UserDeactivated</c>. Mengembalikan false bila event sudah usang.</summary>
    public bool Deactivate(DateTimeOffset occurredAt)
    {
        if (occurredAt < LastEventAt) return false;

        IsActive = false;
        LastEventAt = occurredAt;
        return true;
    }
}
