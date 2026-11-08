using MediatR;
using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.User.Application.Abstractions;

namespace NexUS.User.Application.Users;

public sealed record GetUserQuery(Guid Id) : IRequest<UserDto>;

public sealed class GetUserQueryHandler(IUserReadService users) : IRequestHandler<GetUserQuery, UserDto>
{
    public async Task<UserDto> Handle(GetUserQuery request, CancellationToken ct) =>
        await users.GetAsync(request.Id, ct) ?? throw new NotFoundException("User tidak ditemukan.");
}

public sealed record GetMeQuery : IRequest<UserDto>;

public sealed class GetMeQueryHandler(IUserReadService users, ICurrentUser currentUser) : IRequestHandler<GetMeQuery, UserDto>
{
    public async Task<UserDto> Handle(GetMeQuery request, CancellationToken ct) =>
        await users.GetAsync(currentUser.UserId, ct) ?? throw new NotFoundException("Profil user tidak ditemukan.");
}
