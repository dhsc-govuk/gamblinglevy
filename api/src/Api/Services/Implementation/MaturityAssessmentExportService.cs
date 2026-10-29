using System.Globalization;
using Api.Services.Abstract;
using Api.Services.ReportSnapshot;
using Application.Common.Csv;
using Application.Common.Interfaces.Queries;
using Application.Common.Permissions;
using Application.MaturityAssessments.Export;
using Application.MaturityAssessments.Snapshots;
using Application.Submissions;
using Domain.Collections;
using Domain.Organisations;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;

namespace Api.Services.Implementation;

// The per-organisation "Download your assessment" ZIP: a summary CSV and one long-format CSV of every visible
// question and its rendered answer, both read from the snapshot frozen at submit time. Only a submitted assessment
// is exportable — the artefact is the immutable copy "for your records".
public class MaturityAssessmentExportService(
    ICollectionQueries collectionQueries,
    IOrganisationQueries organisationQueries,
    IMaturityAssessmentQueries assessmentQueries) : IMaturityAssessmentExportService
{
    private const string LocalAuthorityColumn = "Local authority";
    private const string NotRecorded = "Not recorded";

    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");
    private static readonly TimeZoneInfo UkTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    public async Task<Option<ReportExportFile>> GenerateAsync(Permission permission, Guid collectionId, CancellationToken cancellationToken)
    {
        var organisationId = permission.OrganisationId.IsNone ? null : permission.OrganisationId.ValueUnsafe();
        if (organisationId is null)
        {
            return Option<ReportExportFile>.None;
        }

        return await GenerateAsync(new CollectionId(collectionId), organisationId, cancellationToken);
    }

    public Task<Option<ReportExportFile>> GenerateForOrganisationAsync(Guid collectionId, Guid organisationId, CancellationToken cancellationToken) =>
        GenerateAsync(new CollectionId(collectionId), new OrganisationId(organisationId), cancellationToken);

    private async Task<Option<ReportExportFile>> GenerateAsync(CollectionId collectionId, OrganisationId organisationId, CancellationToken cancellationToken)
    {
        var collectionLookup = await collectionQueries.GetByIdAsync(collectionId, cancellationToken: cancellationToken);
        if (collectionLookup.IsNone)
        {
            return Option<ReportExportFile>.None;
        }

        var organisationLookup = await organisationQueries.GetOrganisationById(organisationId, cancellationToken);
        if (organisationLookup.IsNone)
        {
            return Option<ReportExportFile>.None;
        }

        var collection = collectionLookup.ValueUnsafe();
        var organisation = organisationLookup.ValueUnsafe();
        if (!SubmissionEligibility.IsMaturityAssessment(collection))
        {
            return Option<ReportExportFile>.None;
        }

        var detailLookup = await assessmentQueries.GetDetailForCollectionAndOrgAsync(collection.Id, organisation.Id, cancellationToken);
        if (detailLookup.IsNone)
        {
            return Option<ReportExportFile>.None;
        }

        // An organisation that is neither assigned nor a submitter cannot reach this branch: an assessment row is
        // only ever created for an assigned organisation, so the row's existence is the assignment check.
        var detail = detailLookup.ValueUnsafe();
        if (detail.SubmittedAt is not { } submittedAt)
        {
            return Option<ReportExportFile>.None;
        }

        var snapshot = MaturityAssessmentSnapshotJson.TryRead(detail.SnapshotJson) ?? new MaturityAssessmentSnapshotDocument([]);
        var name = organisation.Name;

        var summaryCsv = BuildSummaryCsv(collection, name, detail.ReferenceNumber ?? string.Empty, submittedAt);
        var answersCsv = CsvWriter.Build(
            [LocalAuthorityColumn, "Section", "Page", "Question", "Answer"],
            MaturityAssessmentCsv.LongRows([name], snapshot));

        var content = ZipWriter.Build(
        [
            ("assessment-summary.csv", summaryCsv),
            ("maturity-assessment.csv", answersCsv),
        ]);

        return new ReportExportFile(BuildFileName(name, collection, detail.ReferenceNumber ?? string.Empty), content);
    }

    private byte[] BuildSummaryCsv(Collection collection, string localAuthorityName, string referenceNumber, DateTime submittedAt)
    {
        var period = collection.ReportingPeriodStart is { } periodStart && collection.ReportingPeriodEnd is { } periodEnd
            ? $"{FormatDate(periodStart)} to {FormatDate(periodEnd)}"
            : NotRecorded;
        var ukSubmittedAt = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(submittedAt, DateTimeKind.Utc), UkTimeZone);

        var rows = new List<IReadOnlyList<string>>();
        rows.Add([localAuthorityName, "Reference number", referenceNumber]);
        rows.Add([localAuthorityName, LocalAuthorityColumn, localAuthorityName]);
        rows.Add([localAuthorityName, "Financial year", string.IsNullOrWhiteSpace(collection.FinancialYear) ? NotRecorded : collection.FinancialYear]);
        rows.Add([localAuthorityName, "Reporting period", period]);
        rows.Add([localAuthorityName, "Submission deadline", collection.SubmissionDeadline is { } deadline ? FormatDate(deadline) : NotRecorded]);
        rows.Add([localAuthorityName, "Submitted", $"{ukSubmittedAt.ToString("d MMMM yyyy 'at' HH:mm", EnGb)} (UK time)"]);

        return CsvWriter.Build([LocalAuthorityColumn, "Field", "Value"], rows);
    }

    private string FormatDate(DateOnly date) => date.ToString("d MMMM yyyy", EnGb);

    // "Manchester-City-Council-maturity-assessment-2026-27.zip"; falls back to the reference when the name slugs
    // away to nothing — a download must never fail over what an organisation is called.
    private static string BuildFileName(string localAuthorityName, Collection collection, string referenceNumber)
    {
        var name = ExportFileName.NameSegment(localAuthorityName);
        if (name.Length == 0)
        {
            return $"maturity-assessment-{referenceNumber}.zip";
        }

        var financialYear = ExportFileName.Slug(collection.FinancialYear ?? string.Empty);
        var year = financialYear.Length == 0 ? string.Empty : $"-{financialYear}";
        return $"{name}-maturity-assessment{year}.zip";
    }
}