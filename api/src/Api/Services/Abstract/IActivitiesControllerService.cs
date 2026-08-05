using Api.Dtos;
using Application.Common.Permissions;
using Domain.Activities;
using LanguageExt;

namespace Api.Services.Abstract;

public interface IActivitiesControllerService
{
    // Maps a domain Activity to its full DTO, enriching with category/task names and expected results.
    Task<ActivityDto> ToDtoAsync(Activity activity, CancellationToken cancellationToken);

    Task<IReadOnlyList<ActivitySummaryDto>> ListAsync(Permission permission, string? status, CancellationToken cancellationToken);
    Task<Option<ActivityDto>> GetAsync(Permission permission, Guid activityId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ActivitySummaryDto>> ReviewAsync(Permission permission, CancellationToken cancellationToken);

    // The organisation's soft-deleted activities — feeds the "Deleted activities" page.
    Task<IReadOnlyList<ActivitySummaryDto>> DeletedAsync(Permission permission, CancellationToken cancellationToken);

    Task<ActivityTaxonomyDto> GetTaxonomyAsync(CancellationToken cancellationToken);
}