using MediatR;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.User.Application.Abstractions;
using NexUS.User.Application.Common;

namespace NexUS.User.Application.Departments;

public sealed record DeleteDepartmentCommand(Guid Id, long ExpectedRowVersion) : IRequest;

public sealed class DeleteDepartmentCommandHandler(IDepartmentRepository departments, IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteDepartmentCommand>
{
    public async Task Handle(DeleteDepartmentCommand request, CancellationToken ct)
    {
        var department = await departments.FindAsync(request.Id, ct) ?? throw new NotFoundException("Departemen tidak ditemukan.");

        if (await departments.HasChildrenAsync(department.Id, ct))
            throw new DomainException("Departemen masih memiliki sub-departemen.");
        if (await departments.HasUsersAsync(department.Id, ct))
            throw new DomainException("Departemen masih memiliki user.");

        department.MarkDeleted(); // soft delete (CONTEXT.md bagian 5)
        unitOfWork.ExpectRowVersion(department, request.ExpectedRowVersion);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
