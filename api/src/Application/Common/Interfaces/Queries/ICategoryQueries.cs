using Domain.Categories;

namespace Application.Common.Interfaces.Queries;

public interface ICategoryQueries
{
    Task<IReadOnlyList<Category>> GetActiveCategoriesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivityTask>> GetActiveTasksAsync(CancellationToken cancellationToken = default);
}