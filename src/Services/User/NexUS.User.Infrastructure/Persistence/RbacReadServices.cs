using Microsoft.EntityFrameworkCore;
using NexUS.BuildingBlocks.Common.Results;
using NexUS.User.Application.Abstractions;
using NexUS.User.Application.Roles;

namespace NexUS.User.Infrastructure.Persistence;

public sealed class RoleReadService(UserDbContext db) : IRoleReadService
{
    public async Task<RoleDto?> GetAsync(Guid publicId, CancellationToken ct)
    {
        var role = await db.Roles.AsNoTracking().Where(r => r.PublicId == publicId).FirstOrDefaultAsync(ct);
        if (role is null) return null;

        var permissions = await PermissionCodesAsync([role.Id], ct);
        return new RoleDto(role.PublicId, role.Code, role.Name, role.Description, role.IsSystem,
            permissions.GetValueOrDefault(role.Id) ?? [], role.RowVersion);
    }

    public async Task<PagedResult<RoleDto>> ListAsync(string? search, int page, int size, CancellationToken ct)
    {
        page = PagedResult<RoleDto>.NormalizePage(page);
        size = PagedResult<RoleDto>.NormalizeSize(size);

        var query = db.Roles.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.ToLowerInvariant();
            query = query.Where(r => r.Name.ToLower().Contains(term) || r.Code.ToLower().Contains(term));
        }

        var total = await query.LongCountAsync(ct);
        var roles = await query.OrderByDescending(r => r.IsSystem).ThenBy(r => r.Name).ThenBy(r => r.Id)
            .Skip((page - 1) * size).Take(size).ToListAsync(ct);

        var permissions = await PermissionCodesAsync(roles.Select(r => r.Id).ToList(), ct);
        var items = roles.Select(r => new RoleDto(r.PublicId, r.Code, r.Name, r.Description, r.IsSystem,
            permissions.GetValueOrDefault(r.Id) ?? [], r.RowVersion)).ToList();

        return new PagedResult<RoleDto>(items, page, size, total);
    }

    private async Task<Dictionary<long, IReadOnlyList<string>>> PermissionCodesAsync(IReadOnlyCollection<long> roleIds, CancellationToken ct)
    {
        var rows = await (from rp in db.RolePermissions.AsNoTracking()
                          join p in db.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
                          where roleIds.Contains(rp.RoleId)
                          select new { rp.RoleId, p.Code }).ToListAsync(ct);

        return rows.GroupBy(x => x.RoleId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Code).Order(StringComparer.Ordinal).ToList());
    }
}

public sealed class UserPermissionReadService(UserDbContext db) : IUserPermissionReadService
{
    public async Task<UserPermissionsDto?> GetAsync(Guid userPublicId, CancellationToken ct)
    {
        var userId = await db.Users.AsNoTracking().Where(u => u.PublicId == userPublicId)
            .Select(u => (long?)u.Id).FirstOrDefaultAsync(ct);
        if (userId is null) return null;

        var assignments = await (from ur in db.UserRoles.AsNoTracking()
                                 join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
                                 where ur.UserId == userId
                                 select new { RoleId = r.PublicId, r.Code, ur.ScopeType, ur.ScopeId }).ToListAsync(ct);

        var granted = await (from ur in db.UserRoles.AsNoTracking()
                             join rp in db.RolePermissions.AsNoTracking() on ur.RoleId equals rp.RoleId
                             join p in db.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
                             where ur.UserId == userId
                             select new { p.Code, ur.ScopeType, ur.ScopeId }).Distinct().ToListAsync(ct);

        return new UserPermissionsDto(
            userPublicId,
            assignments.OrderBy(a => a.Code, StringComparer.Ordinal).ThenBy(a => a.ScopeType)
                .Select(a => new UserRoleDto(a.RoleId, a.Code, ScopeTypeNames.ToApi(a.ScopeType), a.ScopeId)).ToList(),
            granted.OrderBy(g => g.Code, StringComparer.Ordinal).ThenBy(g => g.ScopeType)
                .Select(g => new GrantedPermissionDto(g.Code, ScopeTypeNames.ToApi(g.ScopeType), g.ScopeId)).ToList());
    }
}
