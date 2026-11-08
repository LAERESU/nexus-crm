using NexUS.BuildingBlocks.Common.Results;
using NexUS.User.Application.Departments;
using NexUS.User.Application.Tenants;
using NexUS.User.Application.Users;
using NexUS.User.Domain.Departments;
using NexUS.User.Domain.Tenants;
using UserEntity = NexUS.User.Domain.Users.User;

namespace NexUS.User.Application.Abstractions;

// Semua port otomatis dibatasi ke tenant user yang sedang login (CONTEXT.md bagian 3 poin 8):
// data tenant lain dianggap tidak ada.

public interface IUserRepository
{
    Task<UserEntity?> FindAsync(Guid publicId, CancellationToken ct);

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
