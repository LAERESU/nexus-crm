namespace NexUS.User.Application.Users;

/// <param name="RowVersion">Dikirim balik klien lewat header <c>If-Match</c> saat mengubah data.</param>
public sealed record UserDto(
    Guid Id,
    string Email,
    string FullName,
    string? JobTitle,
    string Status,
    Guid? DepartmentId,
    string? DepartmentName,
    long RowVersion,
    DateTimeOffset CreatedAt);
