using FluentValidation;
using MediatR;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.User.Application.Abstractions;
using NexUS.User.Application.Common;
using NexUS.User.Domain.Departments;

namespace NexUS.User.Application.Departments;

public sealed record UpdateDepartmentCommand(Guid Id, string Name, Guid? ParentId, long ExpectedRowVersion) : IRequest<DepartmentDto>;

public sealed class UpdateDepartmentCommandValidator : AbstractValidator<UpdateDepartmentCommand>
{
    public UpdateDepartmentCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Department.NameMaxLength);
        RuleFor(x => x.ExpectedRowVersion).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateDepartmentCommandHandler(
    IDepartmentRepository departments,
    IDepartmentReadService readService,
    IUnitOfWork unitOfWork) : IRequestHandler<UpdateDepartmentCommand, DepartmentDto>
{
    public async Task<DepartmentDto> Handle(UpdateDepartmentCommand request, CancellationToken ct)
    {
        var department = await departments.FindAsync(request.Id, ct) ?? throw new NotFoundException("Departemen tidak ditemukan.");

        long? parentId = null;
        IReadOnlyCollection<long> ancestors = [];
        if (request.ParentId is { } publicParentId)
        {
            var parent = await departments.FindAsync(publicParentId, ct)
                ?? throw new NotFoundException("Departemen induk tidak ditemukan.");
            parentId = parent.Id;
            ancestors = await departments.GetSelfAndAncestorIdsAsync(parent.Id, ct);
        }

        department.Rename(request.Name);
        department.MoveTo(parentId, ancestors);
        unitOfWork.ExpectRowVersion(department, request.ExpectedRowVersion);
        await unitOfWork.SaveChangesAsync(ct);

        return await readService.GetAsync(department.PublicId, ct)
            ?? throw new InvalidOperationException("Departemen yang baru diubah tidak terbaca.");
    }
}
