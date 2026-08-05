using Domain.Activities;

namespace Application.Common.Interfaces.Repositories;

public interface IActivityExpectedResultRepository
{
    Task<ActivityExpectedResult> AddAsync(ActivityExpectedResult entity, CancellationToken cancellationToken = default);
    Task<ActivityExpectedResult> UpdateAsync(ActivityExpectedResult entity, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivityExpectedResult>> GetForActivityAsync(ActivityId activityId, CancellationToken cancellationToken = default);
    Task<int> RemoveRangeAsync(IReadOnlyList<ActivityExpectedResult> entities, CancellationToken cancellationToken = default, bool saveChanges = true);
}