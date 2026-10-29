using System.Text.Json;
using Application.Common.Interfaces.Queries;
using Domain.Collections;
using Domain.DataCollectionForms;

namespace Application.Collections.Audit;

internal static class CollectionSnapshot
{
    // BLUM-9106: the payload version of a collection audit row that carries formVersions. Version 1 rows (every row
    // written before, and every row while BLUM_9087 is off) have no formVersions and are never rewritten.
    public const int FormVersionsPayloadVersion = 2;

    // With versionNumbers (BLUM-9106, behind BLUM_9087) the snapshot also records the version of each form:
    // formVersions = [{ moduleId, versionId, versionNumber }], null ids while a form records none. A row that
    // serialises it passes FormVersionsPayloadVersion. Without it the shape is exactly the version 1 shape. Rows written
    // while DHSC could choose a version (removed in BLUM-9103) also carry a `chosen` flag per entry; nothing reads it.
    public static string Serialize(Collection collection, DateTime utcNow, IReadOnlyDictionary<FormModuleVersionId, int>? versionNumbers = null)
    {
        var formVersions = versionNumbers is null
            ? null
            : collection.Forms.Select(f => new
            {
                moduleId = f.FormModuleId.Value,
                versionId = f.FormModuleVersionId?.Value,
                versionNumber = f.FormModuleVersionId is { } id && versionNumbers.TryGetValue(id, out var number) ? number : (int?)null,
            }).ToList();

        return formVersions is null ? SerializeV1(collection, utcNow) : JsonSerializer.Serialize(new
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
            status = collection.GetStatus(utcNow).ToString(),
            isDeleted = collection.IsDeleted,
            deletedAt = collection.DeletedAt,
            forms = collection.Forms.Select(f => f.FormModuleId.Value),
            formVersions,
            localAuthorities = collection.LocalAuthorities.Select(la => la.LocalAuthorityId.Value),
            reminders = collection.Reminders.Select(r => new { offsetType = r.OffsetType.ToString(), r.Days }),
        });
    }

    // The version numbers behind the versions a collection records, for Serialize's formVersions.
    public static async Task<IReadOnlyDictionary<FormModuleVersionId, int>> VersionNumbersAsync(
        Collection collection,
        IFormModuleVersionQueries versionQueries,
        CancellationToken cancellationToken) =>
        await versionQueries.GetVersionNumbersAsync(
            collection.Forms.Where(f => f.FormModuleVersionId is not null).Select(f => f.FormModuleVersionId!).ToList(),
            cancellationToken);

    private static string SerializeV1(Collection collection, DateTime utcNow)
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
            status = collection.GetStatus(utcNow).ToString(),
            isDeleted = collection.IsDeleted,
            deletedAt = collection.DeletedAt,
            forms = collection.Forms.Select(f => f.FormModuleId.Value),
            localAuthorities = collection.LocalAuthorities.Select(la => la.LocalAuthorityId.Value),
            reminders = collection.Reminders.Select(r => new { offsetType = r.OffsetType.ToString(), r.Days }),
        });
    }
}