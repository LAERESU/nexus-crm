using FluentValidation;
using MediatR;
using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.User.Application.Abstractions;
using NexUS.User.Application.Common;
using NexUS.User.Domain.Departments;

namespace NexUS.User.Application.Departments;

public sealed record CreateDepartmentCommand(string Code, string Name, Guid? ParentId) : IRequest<DepartmentDto>;

public sealed class CreateDepartmentCommandValidator : AbstractValidator<CreateDepartmentCommand>
{
    public CreateDepartmentCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(Department.CodeMaxLength);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Department.NameMaxLength);
    }
}

public sealed class CreateDepartmentCommandHandler(
    IDepartmentRepository departments,
    IDepartmentReadService readService,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : IRequestHandler<CreateDepartmentCommand, DepartmentDto>
{
    public async Task<DepartmentDto> Handle(CreateDepartmentCommand request, CancellationToken ct)
    {
        var code = Department.NormalizeCode(request.Code);
        if (await departments.CodeExistsAsync(code, exceptId: null, ct))
            throw new DomainException("Kode departemen sudah dipakai di tenant ini.");

        long? parentId = null;
        if (request.ParentId is { } publicParentId)
        {
            var parent = await departments.FindAsync(publicParentId, ct)
                ?? throw new NotFoundException("Departemen induk tidak ditemukan.");
            parentId = parent.Id;
        }

        var department = Department.Create(currentUser.TenantId, code, request.Name, parentId);
        await departments.AddAsync(department, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return await readService.GetAsync(department.PublicId, ct)
            ?? throw new InvalidOperationException("Departemen yang baru dibuat tidak terbaca.");
    }
}
