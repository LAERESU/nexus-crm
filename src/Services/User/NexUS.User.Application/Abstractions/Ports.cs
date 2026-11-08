using NexUS.BuildingBlocks.Common.Results;
using NexUS.User.Application.Departments;
using NexUS.User.Application.Roles;
using NexUS.User.Application.Tenants;
using NexUS.User.Application.Users;
using NexUS.User.Domain.Departments;
using NexUS.User.Domain.Roles;
using NexUS.User.Domain.Tenants;
using UserEntity = NexUS.User.Domain.Users.User;

namespace NexUS.User.Application.Abstractions;

// Semua port otomatis dibatasi ke tenant user yang sedang login (CONTEXT.md bagian 3 poin 8):
// data tenant lain dianggap tidak ada.

public interface IUserRepository
{
    Task<UserEntity?> FindAsync(Guid publicId, CancellationToken ct);

    /// <summary>Seperti <see cref="FindAsync"/> dengan penugasan role dimuat (untuk diubah).</summary>
    Task<UserEntity?> FindWithRolesAsync(Guid publicId, CancellationToken ct);

    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken ct);

    Task AddAsync(UserEntity user, CancellationToken ct);
}

public interface IUserReadService
{
    Task<UserDto?> GetAsync(Guid publicId, CancellationToken ct);

    Task<PagedResult<UserDto>> ListAsync(UserFilter filter, CancellationToken ct);
}

public sealed record UserFilter(string? Search, Guid? DepartmentId, string? Status, int Page, int Size);

public interface IDepartmentRepository
{
    Task<Department?> FindAsync(Guid publicId, CancellationToken ct);

    Task<bool> CodeExistsAsync(string normalizedCode, long? exceptId, CancellationToken ct);

    /// <summary>ID internal departemen <paramref name="id"/> beserta seluruh leluhurnya sampai akar.</summary>
    Task<IReadOnlyCollection<long>> GetSelfAndAncestorIdsAsync(long id, CancellationToken ct);

    Task<bool> HasChildrenAsync(long id, CancellationToken ct);

    Task<bool> HasUsersAsync(long id, CancellationToken ct);

    Task AddAsync(Department department, CancellationToken ct);
}

public interface IDepartmentReadService
{
    Task<DepartmentDto?> GetAsync(Guid publicId, CancellationToken ct);

    Task<PagedResult<DepartmentDto>> ListAsync(string? search, int page, int size, CancellationToken ct);
}

public interface ITenantRepository
{
    /// <summary>Tenant user yang sedang login.</summary>
    Task<Tenant?> GetCurrentAsync(CancellationToken ct);
}

public interface IRoleRepository
{
    /// <summary>Role beserta permission-nya (dilacak, untuk diubah).</summary>
    Task<Role?> FindAsync(Guid publicId, CancellationToken ct);

    Task<IReadOnlyList<Role>> FindManyAsync(IReadOnlyCollection<Guid> publicIds, CancellationToken ct);

    Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken ct);

    /// <summary>True bila role masih ditugaskan ke minimal satu user.</summary>
    Task<bool> HasAssignmentsAsync(long roleId, CancellationToken ct);

    Task AddAsync(Role role, CancellationToken ct);
}

public interface IPermissionRepository
{
    Task<IReadOnlyList<Permission>> ListAsync(CancellationToken ct);

    Task<IReadOnlyList<Permission>> FindByCodesAsync(IReadOnlyCollection<string> codes, CancellationToken ct);
}

public interface IRoleReadService
{
    Task<RoleDto?> GetAsync(Guid publicId, CancellationToken ct);

    Task<PagedResult<RoleDto>> ListAsync(string? search, int page, int size, CancellationToken ct);
}

public interface IUserPermissionReadService
{
    /// <summary>Akses efektif user, dihitung dari database pada saat dipanggil (tanpa cache).</summary>
    Task<UserPermissionsDto?> GetAsync(Guid userPublicId, CancellationToken ct);
}
