using Domain.Collections;

namespace Application.Common.Interfaces.Repositories;

public interface ICollectionRepository
{
    Task<Collection> AddAsync(Collection collection, CancellationToken cancellationToken = default);
    Task UpdateAsync(Collection collection, CancellationToken cancellationToken = default);
}