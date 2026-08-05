using Domain.Activities;

namespace Application.Common.Interfaces.Repositories;

public interface IActivityAnswerRepository
{
    Task<ActivityAnswer> AddAsync(ActivityAnswer entity, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivityAnswer>> GetForActivityAsync(ActivityId activityId, CancellationToken cancellationToken = default);

    // Removes only the activity's answers for the given question codes — answers outside the set (questions in
    // sections scoped to a different activity type, or questions since deleted from the schema) must survive an
    // update untouched (BLUM-6933).
    Task RemoveByActivityAsync(ActivityId activityId, IReadOnlyCollection<string> questionCodes, CancellationToken cancellationToken = default);
}