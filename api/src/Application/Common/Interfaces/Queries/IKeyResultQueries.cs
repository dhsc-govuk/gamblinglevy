using Domain.Categories;
using Domain.KeyResults;

namespace Application.Common.Interfaces.Queries;

public interface IKeyResultQueries
{
    // Active standard key results for one activity type — feeds the activity wizard's checkbox list and validates
    // selections on save.
    Task<IReadOnlyList<KeyResultDefinition>> GetDefinitionsForTaskAsync(ActivityTaskId activityTaskId, CancellationToken cancellationToken = default);

    // All active definitions — feeds the activity taxonomy endpoint (the wizard loads the full set once).
    Task<IReadOnlyList<KeyResultDefinition>> GetAllDefinitionsAsync(CancellationToken cancellationToken = default);

    // Active protocol questions for one activity type, every status variant — the resolver filters by scope.
    Task<IReadOnlyList<KeyResultQuestion>> GetQuestionsForTaskAsync(ActivityTaskId activityTaskId, CancellationToken cancellationToken = default);
}