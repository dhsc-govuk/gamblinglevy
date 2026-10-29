using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Domain.Submissions;

namespace Application.Submissions.Audit;

// Audit before/after shape for a quarterly-report submission (BLUM-8747). Workflow state only: ids, timestamps,
// actors, the reference number and per-section statuses. Never answers, amounts or any other text the LA entered —
// the frozen report snapshot is represented by a hash so a regeneration is visible without copying its content.
internal static class SubmissionSnapshot
{
    public static string Serialize(Submission submission) => JsonSerializer.Serialize(Shape(submission));

    // The same shape plus the activity the change was made for, for the per-activity "Enter activity results" steps.
    public static string Serialize(Submission submission, Guid activityId)
    {
        var shape = Shape(submission);
        shape["activityId"] = activityId;
        return JsonSerializer.Serialize(shape);
    }

    private static Dictionary<string, object?> Shape(Submission submission)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = submission.Id.Value,
            ["collectionId"] = submission.CollectionId.Value,
            ["organisationId"] = submission.OrganisationId.Value,
            ["areActivitiesConfirmed"] = submission.AreActivitiesConfirmed,
            ["activitiesConfirmedAt"] = submission.ActivitiesConfirmedAt,
            ["activitiesConfirmedBy"] = submission.ActivitiesConfirmedBy?.Value,
            ["activitiesReviewRequired"] = submission.ActivitiesReviewRequired,
            ["isSubmitted"] = submission.IsSubmitted,
            ["submittedAt"] = submission.SubmittedAt,
            ["submittedBy"] = submission.SubmittedBy?.Value,
            ["referenceNumber"] = submission.ReferenceNumber,
            ["reportSnapshotSha256"] = Hash(submission.ReportSnapshotJson),
            ["sections"] = submission.Sections
                .OrderBy(s => s.ModuleCode, StringComparer.Ordinal)
                .Select(s => new { moduleCode = s.ModuleCode, status = s.Status.ToString(), reviewRequired = s.ReviewRequired })
                .ToList(),
        };
    }

    private static string? Hash(string? value) =>
        value is null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}