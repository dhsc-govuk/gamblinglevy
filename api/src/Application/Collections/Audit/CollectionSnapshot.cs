using System.Text.Json;
using Domain.Collections;

namespace Application.Collections.Audit;

internal static class CollectionSnapshot
{
    public static string Serialize(Collection collection)
    {
        return JsonSerializer.Serialize(new
        {
            id = collection.Id.Value,
            name = collection.Name,
            purpose = collection.Purpose,
            type = collection.Type?.ToString(),
            financialYear = collection.FinancialYear,
            quarter = collection.Quarter,
            reportingPeriodStart = collection.ReportingPeriodStart,
            reportingPeriodEnd = collection.ReportingPeriodEnd,
            windowSameAsReportingPeriod = collection.WindowSameAsReportingPeriod,
            collectionOpenDate = collection.CollectionOpenDate,
            submissionDeadline = collection.SubmissionDeadline,
            isSubmittedByAllLas = collection.IsSubmittedByAllLas,
            isDraft = collection.IsDraft,
            isManuallyClosed = collection.IsManuallyClosed,
            status = collection.GetStatus().ToString(),
            isDeleted = collection.IsDeleted,
            deletedAt = collection.DeletedAt,
            forms = collection.Forms.Select(f => f.FormModuleId.Value),
            localAuthorities = collection.LocalAuthorities.Select(la => la.LocalAuthorityId.Value),
            reminders = collection.Reminders.Select(r => new { offsetType = r.OffsetType.ToString(), r.Days }),
        });
    }
}