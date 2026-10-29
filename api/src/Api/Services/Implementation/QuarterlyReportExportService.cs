using System.Globalization;
using Api.Services.Abstract;
using Api.Services.ReportSnapshot;
using Application.Common.Csv;
using Application.Common.Features;
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
    ISubmissionQueries submissionQueries,
    IFeatureFlags featureFlags) : IQuarterlyReportExportService
{
    // Every collection setup field is nullable on the entity: a quarterly collection can be saved as a partial draft,
    // and an admin can revert a published one to draft and clear it. A submitted report against such a collection is
    // unusual but not impossible, and null-forgiving those reads in BuildSummaryCsv took the whole download down with
    // a 500 (BLUM-7216 follow-up). A missing field is reported as missing — the rest of the report is still worth
    // having.
    private const string NotRecorded = "Not recorded";

    // The identity column prepended to every row of every CSV in the ZIP (BLUM-7647). "Local authority" rather than
    // "Organisation" to match the field label report-summary.csv has always used.
    private const string LocalAuthorityColumn = "Local authority";

    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");
    private static readonly TimeZoneInfo UkTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    public Task<Option<ReportExportFile>> GenerateAsync(Permission permission, Guid collectionId, CancellationToken cancellationToken) =>
        GenerateForCallerAsync(permission, collectionId, allowLiveRender: true, cancellationToken);

    public Task<Option<ReportExportFile>> GenerateFromFrozenSnapshotAsync(Permission permission, Guid collectionId, CancellationToken cancellationToken) =>
        GenerateForCallerAsync(permission, collectionId, allowLiveRender: false, cancellationToken);

    private async Task<Option<ReportExportFile>> GenerateForCallerAsync(
        Permission permission,
        Guid collectionId,
        bool allowLiveRender,
        CancellationToken cancellationToken)
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

        if (!allowLiveRender)
        {
            var frozen = TryDeserializeSnapshot(submission.ReportSnapshotJson);
            return frozen is null ? Option<ReportExportFile>.None : BuildExportFile(collection, organisation.Name, submission, frozen);
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
        => BuildExportFile(
            collection,
            localAuthorityName,
            submission.ReferenceNumber!,
            submission.SubmittedAt!.Value,
            submission.Sections.Select(s => s.ModuleCode).ToHashSet(StringComparer.Ordinal),
            snapshot);

    private ReportExportFile BuildExportFile(
        Collection collection,
        string localAuthorityName,
        string referenceNumber,
        DateTime submittedAt,
        IReadOnlySet<string> moduleCodes,
        ReportSnapshotDocument snapshot)
    {
        // One organisation, so the shared row builders get a single identity cell — the cross-organisation export
        // passes {Organisation, Region, ONS code, E-code} instead and otherwise produces the same rows. The name is
        // on every row (BLUM-7647) because a downloaded file gets moved, renamed and mailed on, and a sheet of
        // answers that does not say whose answers they are is unusable once that happens.
        ReportSnapshotCsvSource[] sources = [new([localAuthorityName], snapshot)];

        // Entry names are a downstream data contract for the analysts who consume the ZIP, pinned by tests.
        // BLUM-7716 renamed activity-key-results.csv to activity-results.csv to match the "Activity results"
        // wording BLUM-7563 introduced; the activity_key_results module code is a stored identifier and stays.
        //
        // A section file is written only when the report HAD that section (BLUM-8106): a form deselected on the
        // collection gives the LA no section for it, so its file does not belong in their copy of the report. Keyed
        // on the submission's sections rather than on the snapshot's content, because an empty snapshot part cannot
        // tell "not part of this report" from "part of it, nothing recorded" — the latter keeps its placeholder file.
        var entries = new List<(string Name, byte[] Content)>
        {
            ("report-summary.csv", BuildSummaryCsv(collection, localAuthorityName, referenceNumber, submittedAt)),
        };

        if (moduleCodes.Contains(SubmissionModules.ActivityKeyResults))
        {
            entries.Add(("activity-results.csv", CsvWriter.Build(
                [LocalAuthorityColumn, "Activity", "Question", "Answer"],
                ReportSnapshotCsv.SectionRows(sources, s => s.ActivitySections))));
        }

        if (moduleCodes.Contains(SubmissionModules.FundingSpent))
        {
            entries.Add(("funding-spend.csv", snapshot.Funding is null
                ? EmptySectionCsv(localAuthorityName, "No funding spend data was recorded.")
                : BuildFundingCsv(sources, snapshot.Funding)));
        }

        if (moduleCodes.Contains(SubmissionModules.StrategicFeedback))
        {
            entries.Add(("strategic-feedback.csv", snapshot.FeedbackSections.Count == 0
                ? EmptySectionCsv(localAuthorityName, "No strategic feedback was recorded.")
                : BuildStrategicFeedbackCsv(localAuthorityName, sources, snapshot)));
        }

        var content = ZipWriter.Build(entries);

        return new ReportExportFile(BuildFileName(localAuthorityName, collection, referenceNumber), content);
    }

    // Behind BLUM_8329 the file is the LA's single row, one column per question (the same answer cells DHSC's window
    // export gives this organisation); otherwise one row per question under a Section column.
    private byte[] BuildStrategicFeedbackCsv(
        string localAuthorityName,
        IReadOnlyList<ReportSnapshotCsvSource> sources,
        ReportSnapshotDocument snapshot)
    {
        if (!featureFlags.IsEnabled(Feature.BLUM_8329))
        {
            return CsvWriter.Build([LocalAuthorityColumn, "Section", "Question", "Answer"], ReportSnapshotCsv.SectionRows(sources, s => s.FeedbackSections));
        }

        // Only reached for a snapshot holding strategic feedback (the caller writes the placeholder otherwise), so null
        // is a broken caller — fail rather than write a one-cell row under a five-column header.
        var answers = ReportSnapshotCsv.StrategicFeedbackAnswers(snapshot)
            ?? throw new InvalidOperationException("The strategic feedback file was built for a snapshot with no strategic feedback.");
        return CsvWriter.Build(
            [LocalAuthorityColumn, .. ReportSnapshotCsv.StrategicFeedbackQuestionColumns],
            [[localAuthorityName, .. answers]]);
    }

    // Null when nothing is stored or the stored JSON does not read as a snapshot document.
    private ReportSnapshotDocument? TryDeserializeSnapshot(string? snapshotJson) =>
        string.IsNullOrEmpty(snapshotJson) ? null : snapshotService.Deserialize(snapshotJson);

    private ReportSnapshotDocument DeserializeSnapshotOrEmpty(string? snapshotJson) =>
        TryDeserializeSnapshot(snapshotJson) ?? new ReportSnapshotDocument([], null, []);

    // Prefer the snapshot frozen at submit time so the finalised report never drifts. Submissions finalised before
    // snapshots existed have none stored — render live from the current data as a best-effort fallback.
    private async Task<ReportSnapshotDocument> GetOrBuildSnapshotAsync(
        string? snapshotJson,
        Permission permission,
        Guid collectionId,
        CancellationToken cancellationToken)
    {
        var stored = TryDeserializeSnapshot(snapshotJson);
        if (stored is not null)
        {
            return stored;
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

        // The identity cell leads every row here too, so all four files in the ZIP share one shape. The
        // "Local authority" field row below is redundant against it and stays: it is the labelled statement of whose
        // report this is, and dropping it would change a file shape nothing asked to change.
        var rows = new List<IReadOnlyList<string>>();
        rows.Add([localAuthorityName, "Reference number", referenceNumber]);
        rows.Add([localAuthorityName, LocalAuthorityColumn, localAuthorityName]);
        rows.Add([localAuthorityName, "Financial year", string.IsNullOrWhiteSpace(collection.FinancialYear) ? NotRecorded : collection.FinancialYear]);
        rows.Add([localAuthorityName, "Quarter", collection.Quarter is { } quarter ? quarter.ToString(CultureInfo.InvariantCulture) : NotRecorded]);
        rows.Add([localAuthorityName, "Reporting period", period]);
        rows.Add([localAuthorityName, "Submission deadline", collection.SubmissionDeadline is { } deadline ? FormatDate(deadline) : NotRecorded]);
        rows.Add([localAuthorityName, "Submitted", $"{ukSubmittedAt.ToString("d MMMM yyyy 'at' HH:mm", EnGb)} (UK time)"]);
        return CsvWriter.Build([LocalAuthorityColumn, "Field", "Value"], rows);
    }

    // The LA's funding file keeps both grains together — the per-activity table, then a spacer, a sub-header and the
    // summary metrics — because it is one small file read top to bottom. The cross-organisation export splits them
    // into two files instead; see ReportSnapshotCsv.
    private static byte[] BuildFundingCsv(IReadOnlyList<ReportSnapshotCsvSource> sources, ReportSnapshotFunding funding)
    {
        var rows = new List<IReadOnlyList<string>>(ReportSnapshotCsv.FundingRows(sources));
        rows.Add([]);
        // The sub-header takes the identity cell as well, so it stays aligned with the summary rows underneath it,
        // which carry the source prefix like every other row.
        rows.Add([LocalAuthorityColumn, .. ReportSnapshotCsv.FundingSummaryColumns]);
        rows.AddRange(ReportSnapshotCsv.FundingSummaryRows(sources));

        // "Row type" closes the header because FundingRows closes every per-activity row and the subtotal with it
        // (BLUM-7668). The spacer and the summary block below stay their own narrower shape, as they already did.
        return CsvWriter.Build([LocalAuthorityColumn, .. funding.Header, ReportSnapshotCsv.RowTypeColumn], rows);
    }

    private string FormatDate(DateOnly date) => date.ToString("d MMMM yyyy", EnGb);

    // The saved file names the organisation and the period it covers (BLUM-7647), e.g.
    // "Manchester-City-Council-Q2-2025-26.zip" — the reference number alone said nothing to anyone holding the file
    // outside the service. MVC's File(..., fileDownloadName) writes both filename and filename*, and CORS exposes
    // Content-Disposition (RateLimitHeaders.All), so the client saves this name rather than its own fallback.
    //
    // Every collection setup field is nullable (see NotRecorded above), so each segment is dropped independently
    // rather than rendering a placeholder into a filename, and a name that slugs away to nothing falls back to the
    // original shape. A download must never fail over what an organisation is called.
    private static string BuildFileName(string localAuthorityName, Collection collection, string referenceNumber)
    {
        var name = ExportFileName.NameSegment(localAuthorityName);
        if (name.Length == 0)
        {
            return $"quarterly-report-{referenceNumber}.zip";
        }

        var quarter = collection.Quarter is { } q ? $"-Q{q.ToString(CultureInfo.InvariantCulture)}" : string.Empty;
        var financialYear = ExportFileName.Slug(collection.FinancialYear ?? string.Empty);
        var year = financialYear.Length == 0 ? string.Empty : $"-{financialYear}";
        return $"{name}{quarter}{year}.zip";
    }

    // A section with nothing in it is still that organisation's file. The two "nothing was recorded" placeholders
    // used to be bare prose, which left the sparsest reports — the ones whose only content is the name of whoever
    // filed them — as the one case a reader could not attribute. They are now a one-row CSV carrying the same
    // sentence behind the same identity cell as every other file in the ZIP.
    private static byte[] EmptySectionCsv(string localAuthorityName, string notice) =>
        CsvWriter.Build([LocalAuthorityColumn, "Note"], [[localAuthorityName, notice]]);
}