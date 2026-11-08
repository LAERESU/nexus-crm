using Microsoft.EntityFrameworkCore;
using NexUS.BuildingBlocks.Common.Results;
using NexUS.User.Application.Abstractions;
using NexUS.User.Application.Departments;
using NexUS.User.Application.Users;
using NexUS.User.Domain.Users;

namespace NexUS.User.Infrastructure.Persistence;

public sealed class UserReadService(UserDbContext db) : IUserReadService
{
    public Task<UserDto?> GetAsync(Guid publicId, CancellationToken ct) =>
        Project(db.Users.AsNoTracking().Where(u => u.PublicId == publicId)).FirstOrDefaultAsync(ct);

    public async Task<PagedResult<UserDto>> ListAsync(UserFilter filter, CancellationToken ct)
    {
        var page = PagedResult<UserDto>.NormalizePage(filter.Page);
        var size = PagedResult<UserDto>.NormalizeSize(filter.Size);

        var query = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            // Pencarian tidak peka huruf besar/kecil; EMAIL sudah huruf kecil.
            var term = filter.Search.ToLowerInvariant();
            query = query.Where(u => u.Email.Contains(term) || u.FullName.ToLower().Contains(term));
        }

        if (filter.DepartmentId is { } departmentId)
            query = query.Where(u => db.Departments.Any(d => d.PublicId == departmentId && d.Id == u.DepartmentId));

        if (filter.Status is { } statusName && UserStatusNames.TryParse(statusName, out var status))
            query = query.Where(u => u.Status == status);

        var total = await query.LongCountAsync(ct);
        var items = await Project(query.OrderBy(u => u.FullName).ThenBy(u => u.Id).Skip((page - 1) * size).Take(size)).ToListAsync(ct);

        return new PagedResult<UserDto>(items, page, size, total);
    }

    private IQueryable<UserDto> Project(IQueryable<Domain.Users.User> users) =>
        users.Select(u => new UserDto(
            u.PublicId,
            u.Email,
            u.FullName,
            u.JobTitle,
            u.Status == UserStatus.Active ? "active" : "inactive",
            db.Departments.Where(d => d.Id == u.DepartmentId).Select(d => (Guid?)d.PublicId).FirstOrDefault(),
            db.Departments.Where(d => d.Id == u.DepartmentId).Select(d => d.Name).FirstOrDefault(),
            u.RowVersion,
            u.CreatedAt));
}

public sealed class DepartmentReadService(UserDbContext db) : IDepartmentReadService
{
    public Task<DepartmentDto?> GetAsync(Guid publicId, CancellationToken ct) =>
        Project(db.Departments.AsNoTracking().Where(d => d.PublicId == publicId)).FirstOrDefaultAsync(ct);

    public async Task<PagedResult<DepartmentDto>> ListAsync(string? search, int page, int size, CancellationToken ct)
    {
        page = PagedResult<DepartmentDto>.NormalizePage(page);
        size = PagedResult<DepartmentDto>.NormalizeSize(size);

        var query = db.Departments.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.ToLowerInvariant();
            query = query.Where(d => d.Name.ToLower().Contains(term) || d.Code.ToLower().Contains(term));
        }

        var total = await query.LongCountAsync(ct);
        var items = await Project(query.OrderBy(d => d.Name).ThenBy(d => d.Id).Skip((page - 1) * size).Take(size)).ToListAsync(ct);

        return new PagedResult<DepartmentDto>(items, page, size, total);
    }

    private IQueryable<DepartmentDto> Project(IQueryable<Domain.Departments.Department> departments) =>
        departments.Select(d => new DepartmentDto(
            d.PublicId,
            d.Code,
            d.Name,
            db.Departments.Where(p => p.Id == d.ParentId).Select(p => (Guid?)p.PublicId).FirstOrDefault(),
            d.RowVersion));
}
