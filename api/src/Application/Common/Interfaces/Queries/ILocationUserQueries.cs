using Application.Common.Permissions;
using Domain.Locations;
using Domain.LocationUsers;
using LanguageExt;

namespace Application.Common.Interfaces.Queries;

public interface ILocationUserQueries
{
    Task<IReadOnlyList<LocationUser>> GetAll(LocationId id, Permission permission, CancellationToken cancellationToken);
    Task<Option<LocationUser>> GetById(LocationUserId id, Permission permission, CancellationToken cancellationToken);
    Task<int> Count(Option<LocationId> id, Permission permission, CancellationToken cancellationToken);
}