using System.Text.Json;
using Domain.Activities;

namespace Application.Activities.Audit;

internal static class ActivitySnapshot
{
    public static string Serialize(Activity activity, IReadOnlyList<ActivityExpectedResult>? expectedResults = null)
    {
        return JsonSerializer.Serialize(new
        {
            id = activity.Id.Value,
            organisationId = activity.OrganisationId.Value,
            title = activity.Title,
            description = activity.Description,
            categoryId = activity.CategoryId?.Value,
            taskId = activity.TaskId?.Value,
            customTaskText = activity.CustomTaskText,
            status = activity.Status?.ToString(),
            isComplete = activity.IsComplete,
            startMonth = activity.StartMonth,
            startYear = activity.StartYear,
            closureReason = activity.ClosureReason,
            isFlaggedForReview = activity.IsFlaggedForReview,
            deletedAt = activity.DeletedAt,
            expectedResults = expectedResults?.Select(r => new { r.KeyResultCode, r.Text, r.DisplayOrder }),
        });
    }
}