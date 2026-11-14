using NexUS.BuildingBlocks.Common.Domain;

namespace NexUS.Project.Domain.Users;

/// <summary>
/// Read model user milik User Service (tabel <c>USER_REF</c>), diperbarui dari event. Event datang lewat antrean
/// terpisah dan bisa tiba tidak berurutan, jadi profil (nama, email) dan status aktif masing-masing punya waktu event
/// terakhir sendiri: perubahan dari event yang lebih lama daripada itu diabaikan, tanpa saling menghalangi.
/// </summary>
public sealed class UserRef : Entity
{
    /// <summary>ID publik user di User Service.</summary>
    public Guid UserId { get; private set; }

    public string? Email { get; private set; }
    public string? FullName { get; private set; }
    public bool IsActive { get; private set; }

    /// <summary>Waktu event yang terakhir mengisi profil; null bila profil belum pernah diterima.</summary>
    public DateTimeOffset? ProfileEventAt { get; private set; }

    /// <summary>Waktu event yang terakhir mengubah status aktif; null bila status belum pernah diterima.</summary>
    public DateTimeOffset? StatusEventAt { get; private set; }

    private UserRef() { } // untuk EF Core

    /// <summary>Baris baru yang belum memuat event apa pun; isi dengan <see cref="ApplyProfile"/> atau <see cref="Deactivate"/>.</summary>
    public static UserRef Create(long tenantId, Guid userId) => new() { TenantId = tenantId, UserId = userId, IsActive = true };

    /// <summary>
    /// Menerapkan profil dari <c>UserCreated</c>/<c>UserUpdated</c>. <paramref name="isActive"/> null (payload lama yang
    /// belum memuat status) tidak mengubah status. Mengembalikan false bila tidak ada yang diterapkan karena event usang.
    /// </summary>
    public bool ApplyProfile(string email, string fullName, bool? isActive, DateTimeOffset occurredAt)
    {
        var applied = false;
        if (ProfileEventAt is null || occurredAt >= ProfileEventAt)
        {
            Email = email;
            FullName = fullName;
            ProfileEventAt = occurredAt;
            applied = true;
        }

        if (isActive is { } active) applied |= ApplyStatus(active, occurredAt);
        return applied;
    }

    /// <summary>Menerapkan <c>UserDeactivated</c>. Mengembalikan false bila event sudah usang.</summary>
    public bool Deactivate(DateTimeOffset occurredAt) => ApplyStatus(false, occurredAt);

    private bool ApplyStatus(bool isActive, DateTimeOffset occurredAt)
    {
        if (StatusEventAt is { } last && occurredAt < last) return false;

        IsActive = isActive;
        StatusEventAt = occurredAt;
        return true;
    }
}
