using Microsoft.EntityFrameworkCore;
using NexUS.User.Application.Abstractions;
using NexUS.User.Domain.Departments;
using NexUS.User.Domain.Roles;
using NexUS.User.Domain.Tenants;
using UserEntity = NexUS.User.Domain.Users.User;

namespace NexUS.User.Infrastructure.Persistence;

// Filter TENANT_ID dipasang sebagai global query filter di UserDbContext, sehingga semua query di sini
// otomatis dibatasi ke tenant user yang sedang login.

public sealed class UserRepository(UserDbContext db) : IUserRepository
{
    public Task<UserEntity?> FindAsync(Guid publicId, CancellationToken ct) =>
        db.Users.FirstOrDefaultAsync(u => u.PublicId == publicId, ct);

    public Task<UserEntity?> FindWithRolesAsync(Guid publicId, CancellationToken ct) =>
        db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.PublicId == publicId, ct);

    public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken ct) =>
        db.Users.AnyAsync(u => u.Email == normalizedEmail, ct);

    public async Task AddAsync(UserEntity user, CancellationToken ct) => await db.Users.AddAsync(user, ct);
}

public sealed class DepartmentRepository(UserDbContext db) : IDepartmentRepository
{
    public Task<Department?> FindAsync(Guid publicId, CancellationToken ct) =>
        db.Departments.FirstOrDefaultAsync(d => d.PublicId == publicId, ct);

    public Task<bool> CodeExistsAsync(string normalizedCode, long? exceptId, CancellationToken ct) =>
        db.Departments.AnyAsync(d => d.Code == normalizedCode && d.Id != exceptId, ct);

    public async Task<IReadOnlyCollection<long>> GetSelfAndAncestorIdsAsync(long id, CancellationToken ct)
    {
        // Hierarki departemen dangkal; menelusuri ke atas per level lebih sederhana daripada query rekursif.
        var ids = new List<long>();
        long? current = id;
        while (current is { } currentId && !ids.Contains(currentId))
        {
            ids.Add(currentId);
            current = await db.Departments.Where(d => d.Id == currentId).Select(d => d.ParentId).FirstOrDefaultAsync(ct);
        }

        return ids;
    }

    public Task<bool> HasChildrenAsync(long id, CancellationToken ct) => db.Departments.AnyAsync(d => d.ParentId == id, ct);

    public Task<bool> HasUsersAsync(long id, CancellationToken ct) => db.Users.AnyAsync(u => u.DepartmentId == id, ct);

    public async Task AddAsync(Department department, CancellationToken ct) => await db.Departments.AddAsync(department, ct);
}

public sealed class TenantRepository(UserDbContext db) : ITenantRepository
{
    public Task<Tenant?> GetCurrentAsync(CancellationToken ct) => db.Tenants.FirstOrDefaultAsync(ct);
}

public sealed class RoleRepository(UserDbContext db) : IRoleRepository
{
    public Task<Role?> FindAsync(Guid publicId, CancellationToken ct) =>
        db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.PublicId == publicId, ct);

    public async Task<IReadOnlyList<Role>> FindManyAsync(IReadOnlyCollection<Guid> publicIds, CancellationToken ct) =>
        await db.Roles.Where(r => publicIds.Contains(r.PublicId)).ToListAsync(ct);

    public Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken ct) =>
        db.Roles.AnyAsync(r => r.Code == normalizedCode, ct);

    public Task<bool> HasAssignmentsAsync(long roleId, CancellationToken ct) =>
        db.UserRoles.AnyAsync(ur => ur.RoleId == roleId, ct);

    public async Task AddAsync(Role role, CancellationToken ct) => await db.Roles.AddAsync(role, ct);
}

public sealed class PermissionRepository(UserDbContext db) : IPermissionRepository
{
    public async Task<IReadOnlyList<Permission>> ListAsync(CancellationToken ct) =>
        await db.Permissions.AsNoTracking().ToListAsync(ct);

    public async Task<IReadOnlyList<Permission>> FindByCodesAsync(IReadOnlyCollection<string> codes, CancellationToken ct) =>
        await db.Permissions.AsNoTracking().Where(p => codes.Contains(p.Code)).ToListAsync(ct);
}
