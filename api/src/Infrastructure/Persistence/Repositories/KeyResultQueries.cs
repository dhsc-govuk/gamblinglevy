using Application.Common.Interfaces.Queries;
using Domain.Categories;
using Domain.KeyResults;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

// Read-only access to the seeded key-result reference data (definitions + protocol questions). Seeding writes
// through the DbContext directly in ApplicationDbContextInitialiser, so no repository surface is needed.
public class KeyResultQueries(ApplicationDbContext dbContext) : IKeyResultQueries
{
    public async Task<IReadOnlyList<KeyResultDefinition>> GetDefinitionsForTaskAsync(
        ActivityTaskId activityTaskId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.KeyResultDefinitions
            .AsNoTracking()
            .Where(d => d.ActivityTaskId == activityTaskId && d.IsActive)
            .OrderBy(d => d.DisplayOrder)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<KeyResultDefinition>> GetAllDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        return await dbContext.KeyResultDefinitions
            .AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.DisplayOrder)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<KeyResultQuestion>> GetQuestionsForTaskAsync(
        ActivityTaskId activityTaskId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.KeyResultQuestions
            .AsNoTracking()
            .Where(q => q.ActivityTaskId == activityTaskId && q.IsActive)
            .OrderBy(q => q.DisplayOrder)
            .ToListAsync(cancellationToken);
    }
}