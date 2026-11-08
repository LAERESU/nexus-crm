namespace NexUS.User.Application.Departments;

public sealed record DepartmentDto(Guid Id, string Code, string Name, Guid? ParentId, long RowVersion);
