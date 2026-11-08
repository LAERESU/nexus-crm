using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;

namespace NexUS.User.Domain.Users;

public enum RoleScopeType
{
    /// <summary>Berlaku di seluruh tenant.</summary>
    Global,

    /// <summary>Berlaku pada satu departemen.</summary>
    Department,

    /// <summary>Berlaku pada satu proyek (layanan Project; ID-nya tidak bisa divalidasi dari sini).</summary>
    Project,
}

/// <summary>Penugasan satu role ke user pada satu scope. <paramref name="ScopeId"/> adalah ID publik departemen/proyek.</summary>
public sealed record RoleAssignment(long RoleId, RoleScopeType ScopeType, Guid? ScopeId)
{
    /// <summary>GLOBAL tidak boleh punya scope; DEPARTMENT dan PROJECT wajib punya.</summary>
    public RoleAssignment Validated()
    {
        if (RoleId <= 0) throw new DomainException("Role wajib diisi.");

        switch (ScopeType)
        {
            case RoleScopeType.Global when ScopeId is not null:
                throw new DomainException("Penugasan role global tidak boleh memiliki scopeId.");
            case not RoleScopeType.Global when ScopeId is null || ScopeId == Guid.Empty:
                throw new DomainException("Penugasan role dengan scope departemen atau proyek wajib memiliki scopeId.");
            default:
                return this;
        }
    }
}

/// <summary>Baris penugasan role milik <see cref="User"/> (ID internal user dan role).</summary>
public sealed class UserRole : Entity
{
    public long UserId { get; private set; }
    public long RoleId { get; private set; }
    public RoleScopeType ScopeType { get; private set; }
    public Guid? ScopeId { get; private set; }

    private UserRole() { } // untuk EF Core

    internal static UserRole Create(long tenantId, RoleAssignment assignment) =>
        new() { TenantId = tenantId, RoleId = assignment.RoleId, ScopeType = assignment.ScopeType, ScopeId = assignment.ScopeId };

    internal bool Matches(RoleAssignment assignment) =>
        RoleId == assignment.RoleId && ScopeType == assignment.ScopeType && ScopeId == assignment.ScopeId;
}
