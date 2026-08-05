using System.Globalization;
using System.Text;
using Api.Services.Abstract;
using Application.Common.Csv;
using Application.Common.Interfaces.Queries;
using Application.Common.Permissions;
using Application.Common.ReportSnapshots;
using Application.Submissions;
using Domain.Collections;
using Domain.Organisations;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;

namespace Api.Services.Implementation;

// Builds the "Download your report" ZIP shown on the confirmation screen: one CSV per section plus a summary. Only a
// finalised (submitted) report is exportable — the artifact is the immutable copy "for your records", so the section
// content comes from the snapshot frozen at submit time (falling back to a live render for legacy submissions). The
// export does no answer rendering of its own; it only formats. CSV + ZIP use the BCL only (no new deps).
public class QuarterlyReportExportService(
    IReportSnapshotService snapshotService,
    ICollectionQueries collectionQueries,
    IOrganisationQueries organisationQueries,
    ISubmissionQueries submissionQueries) : IQuarterlyReportExportService
{
    // Every collection setup field is nullable on the entity: a quarterly collection can be saved as a partial draft,
    // and an admin can revert a published one to draft and clear it. A submitted report against such a collection is
    // unusual but not impossible, and null-forgiving those reads in BuildSummaryCsv took the whole download down with
    // a 500 (BLUM-7216 follow-up). A missing field is reported as missing — the rest of the report is still worth
    // having.
    private const string NotRecorded = "Not recorded";

    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");
    private static readonly TimeZoneInfo UkTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    public async Task<Option<ReportExportFile>> GenerateAsync(Permission permission, Guid collectionId, CancellationToken cancellationToken)
    {
        // IsNone/ValueUnsafe, not Match — Option.Match throws on a null result (see
        // ActivitiesControllerService.ResolveOrg), which would defeat the null guard immediately below.
        var organisationId = permission.OrganisationId.IsNone ? null : permission.OrganisationId.ValueUnsafe();
        if (organisationId is null)
        {
            return Option<ReportExportFile>.None;
        }

        var collectionLookup = await collectionQueries.GetByIdAsync(new CollectionId(collectionId), cancellationToken: cancellationToken);
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
        if (!SubmissionEligibility.IsQuarterly(collection) || !SubmissionEligibility.IsAssignedTo(collection, organisation))
        {
            return Option<ReportExportFile>.None;
        }

        var submissionLookup = await submissionQueries.GetByCollectionAndOrgAsync(collection.Id, organisationId, cancellationToken);
        if (submissionLookup.IsNone)
        {
            return Option<ReportExportFile>.None;
        }

        var submission = submissionLookup.ValueUnsafe();
        if (!submission.IsSubmitted)
        {
            return Option<ReportExportFile>.None;
        }

        var snapshot = await GetOrBuildSnapshotAsync(submission.ReportSnapshotJson, permission, collectionId, cancellationToken);
        return BuildExportFile(collection, organisation.Name, submission, snapshot);
    }

    public async Task<Option<ReportExportFile>> GenerateForOrganisationAsync(
        Guid collectionId,
        Guid organisationId,
        CancellationToken cancellationToken)
    {
        var collectionLookup = await collectionQueries.GetByIdAsync(new CollectionId(collectionId), cancellationToken: cancellationToken);
        if (collectionLookup.IsNone)
        {
            return Option<ReportExportFile>.None;
        }

        var orgId = new OrganisationId(organisationId);
        var organisationLookup = await organisationQueries.GetOrganisationById(orgId, cancellationToken);
        if (organisationLookup.IsNone)
        {
            return Option<ReportExportFile>.None;
        }

        var collection = collectionLookup.ValueUnsafe();
        var organisation = organisationLookup.ValueUnsafe();
        if (!SubmissionEligibility.IsQuarterly(collection) || !SubmissionEligibility.IsAssignedTo(collection, organisation))
        {
            return Option<ReportExportFile>.None;
        }

        var submissionLookup = await submissionQueries.GetByCollectionAndOrgAsync(collection.Id, orgId, cancellationToken);
        if (submissionLookup.IsNone)
        {
            return Option<ReportExportFile>.None;
        }

        var submission = submissionLookup.ValueUnsafe();
        if (!submission.IsSubmitted)
        {
            return Option<ReportExportFile>.None;
        }

        // Admin caller has no organisation Permission, so the live snapshot fallback is unavailable. Submitted reports
        // always carry a frozen snapshot; legacy pre-snapshot submissions export an empty document.
        var snapshot = DeserializeSnapshotOrEmpty(submission.ReportSnapshotJson);
        return BuildExportFile(collection, organisation.Name, submission, snapshot);
    }

    private ReportExportFile BuildExportFile(
        Collection collection,
        string localAuthorityName,
        Domain.Submissions.Submission submission,
        ReportSnapshotDocument snapshot)
        => BuildExportFile(collection, localAuthorityName, submission.ReferenceNumber!, submission.SubmittedAt!.Value, snapshot);

    private ReportExportFile BuildExportFile(
        Collection collection,
        string localAuthorityName,
        string referenceNumber,
        DateTime submittedAt,
        ReportSnapshotDocument snapshot)
    {
        // One organisation, so the shared row builders get an empty identity prefix — the cross-organisation export
        // passes {Organisation, ONS code, E-code} instead and otherwise produces the same rows.
        ReportSnapshotCsvSource[] sources = [new([], snapshot)];

        var summaryCsv = BuildSummaryCsv(collection, localAuthorityName, referenceNumber, submittedAt);
        var keyResultsCsv = CsvWriter.Build(
            ["Activity", "Question", "Answer"],
            ReportSnapshotCsv.SectionRows(sources, s => s.ActivitySections));
        var fundingCsv = snapshot.Funding is null
            ? Utf8("No funding spend data was recorded.")
            : BuildFundingCsv(sources, snapshot.Funding);
        var feedbackCsv = snapshot.FeedbackSections.Count == 0
            ? Utf8("No strategic feedback was recorded.")
            : CsvWriter.Build(["Section", "Question", "Answer"], ReportSnapshotCsv.SectionRows(sources, s => s.FeedbackSections));

        var content = ZipWriter.Build(
        [
            ("report-summary.csv", summaryCsv),
            ("activity-key-results.csv", keyResultsCsv),
            ("funding-spend.csv", fundingCsv),
            ("strategic-feedback.csv", feedbackCsv),
        ]);

        var fileName = $"quarterly-report-{referenceNumber}.zip";
        return new ReportExportFile(fileName, content);
    }

    private ReportSnapshotDocument DeserializeSnapshotOrEmpty(string? snapshotJson)
    {
        if (!string.IsNullOrEmpty(snapshotJson))
        {
            var stored = snapshotService.Deserialize(snapshotJson);
            if (stored is not null)
            {
                return stored;
            }
        }

        return new ReportSnapshotDocument([], null, []);
    }

    // Prefer the snapshot frozen at submit time so the finalised report never drifts. Submissions finalised before
    // snapshots existed have none stored — render live from the current data as a best-effort fallback.
    private async Task<ReportSnapshotDocument> GetOrBuildSnapshotAsync(
        string? snapshotJson,
        Permission permission,
        Guid collectionId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(snapshotJson))
        {
            var stored = snapshotService.Deserialize(snapshotJson);
            if (stored is not null)
            {
                return stored;
            }
        }

        return (await snapshotService.BuildAsync(permission, collectionId, cancellationToken))
            .Match(s => s, () => new ReportSnapshotDocument([], null, []));
    }

    private byte[] BuildSummaryCsv(Collection collection, string localAuthorityName, string referenceNumber, DateTime submittedAt)
    {
        var period = collection.ReportingPeriodStart is { } periodStart && collection.ReportingPeriodEnd is { } periodEnd
            ? $"{FormatDate(periodStart)} to {FormatDate(periodEnd)}"
            : NotRecorded;
        var ukSubmittedAt = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(submittedAt, DateTimeKind.Utc), UkTimeZone);

        var rows = new List<IReadOnlyList<string>>();
        rows.Add(["Reference number", referenceNumber]);
        rows.Add(["Local authority", localAuthorityName]);
        rows.Add(["Financial year", string.IsNullOrWhiteSpace(collection.FinancialYear) ? NotRecorded : collection.FinancialYear]);
        rows.Add(["Quarter", collection.Quarter is { } quarter ? quarter.ToString(CultureInfo.InvariantCulture) : NotRecorded]);
        rows.Add(["Reporting period", period]);
        rows.Add(["Submission deadline", collection.SubmissionDeadline is { } deadline ? FormatDate(deadline) : NotRecorded]);
        rows.Add(["Submitted", $"{ukSubmittedAt.ToString("d MMMM yyyy 'at' HH:mm", EnGb)} (UK time)"]);
        return CsvWriter.Build(["Field", "Value"], rows);
    }

    // The LA's funding file keeps both grains together — the per-activity table, then a spacer, a sub-header and the
    // summary metrics — because it is one small file read top to bottom. The cross-organisation export splits them
    // into two files instead; see ReportSnapshotCsv.
    private static byte[] BuildFundingCsv(IReadOnlyList<ReportSnapshotCsvSource> sources, ReportSnapshotFunding funding)
    {
        var rows = new List<IReadOnlyList<string>>(ReportSnapshotCsv.FundingRows(sources));
        rows.Add([]);
        rows.Add(ReportSnapshotCsv.FundingSummaryColumns);
        rows.AddRange(ReportSnapshotCsv.FundingSummaryRows(sources));
        return CsvWriter.Build(funding.Header, rows);
    }

    private string FormatDate(DateOnly date) => date.ToString("d MMMM yyyy", EnGb);

    // The two "nothing was recorded" placeholders are prose, not CSV, so they bypass CsvWriter. UTF-8 without a BOM,
    // matching every other entry in the ZIP.
    private static byte[] Utf8(string text) => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
}