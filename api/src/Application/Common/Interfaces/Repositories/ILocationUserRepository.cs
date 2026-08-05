using Application.Common.Permissions;
using Domain.LocationUsers;
using Domain.Users;
using LanguageExt;

namespace Application.Common.Interfaces.Repositories;

public interface ILocationUserRepository
{
    Task<Option<LocationUser>> GetById(LocationUserId id, Permission permission, CancellationToken cancellationToken = default);

    Task<LocationUser> AddAsync(LocationUser entity, CancellationToken cancellationToken = default);

    Task RemoveByUserIdAsync(UserId userId, CancellationToken cancellationToken = default);
}