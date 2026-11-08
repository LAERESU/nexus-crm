using FluentValidation;
using MediatR;
using NexUS.BuildingBlocks.Common.Results;
using NexUS.User.Application.Abstractions;

namespace NexUS.User.Application.Users;

public sealed record ListUsersQuery(string? Search, Guid? DepartmentId, string? Status, int Page = 1, int Size = PagedResult<UserDto>.DefaultSize)
    : IRequest<PagedResult<UserDto>>;

public sealed class ListUsersQueryValidator : AbstractValidator<ListUsersQuery>
{
    public ListUsersQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.Size).InclusiveBetween(1, PagedResult<UserDto>.MaxSize);
        RuleFor(x => x.Status)
            .Must(s => s is null || UserStatusNames.TryParse(s, out _))
            .WithMessage("Status harus 'active' atau 'inactive'.");
    }
}

public sealed class ListUsersQueryHandler(IUserReadService users) : IRequestHandler<ListUsersQuery, PagedResult<UserDto>>
{
    public Task<PagedResult<UserDto>> Handle(ListUsersQuery request, CancellationToken ct) =>
        users.ListAsync(new UserFilter(request.Search?.Trim(), request.DepartmentId, request.Status, request.Page, request.Size), ct);
}
