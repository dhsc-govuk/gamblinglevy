using System.Globalization;
using System.Text.Json;
using Application.Collections.Exceptions;
using Application.Common.Csv;
using Application.Common.Features;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Common.ReportSnapshots;
using Application.DataCollectionForms.Versions;
using Application.Submissions;
using Domain.AuditLogs;
using Domain.Collections;
using Domain.DataCollectionForms;
using Domain.Organisations;
using Domain.Users;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;

namespace Application.Collections.Commands.ExportSubmissions;

// DHSC submission export (BLUM-6100): a ZIP of per-section CSVs for a reporting window, aggregated across every
// organisation. summary.csv keeps the flat per-organisation summary (name, status, submitted date, total reported
// spend, financial year, quarter) covering submitters and non-submitters alike; activity-results.csv,
// funding-spend.csv, funding-summary.csv and strategic-feedback.csv carry the full question-and-answer data from each
// submitter's frozen report snapshot (Sections 1-3). Non-submitters have no snapshot, so they appear in the summary
// only. The export is a command rather than a controller-service read because it must be audited (AC5): audit writes
// live in command handlers by convention, never in controllers/Api services.
//
// Every file here is ONE rectangle — a single header and rows of matching width. The LA's own report ZIP puts the
// funding table and its summary metrics in one file separated by a sub-header, which is fine for a small file a human
// reads top to bottom; repeating that across ~150 organisations put each summary amount under a per-activity money
// column, so summing that column double-counted grant allocations and totals. The summary metrics are a different
// grain and get their own file.
// Region is optional and narrows the export to organisations in that region (BLUM-7682). Null means every
// organisation, which is the shape the file had before the filter existed — an all-regions export is still
// byte-for-byte what it was.
public record ExportSubmissionsCommand(Guid CollectionId, string? Region = null) : IRequest<Either<CollectionException, SubmissionsExportFile>>;

public record SubmissionsExportFile(string FileName, byte[] Content);

public class ExportSubmissionsCommandHandler(
    ICollectionQueries collectionQueries,
    ISubmissionQueries submissionQueries,
    IOrganisationQueries organisationQueries,
    IFormModuleQueries formModuleQueries,
    IFormModuleVersionQueries formModuleVersionQueries,
    ISubmissionSpendEntryRepository spendEntryRepository,
    IAuditLogRepository auditLogRepository,
    PermissionsService permissionsService,
    IFeatureFlags featureFlags,
    IDateTimeProvider dateTimeProvider)
    : IRequestHandler<ExportSubmissionsCommand, Either<CollectionException, SubmissionsExportFile>>
{
    private const string RegionColumn = "Region";

    private static readonly string[] Header =
    [
        "Organisation",
        "Region",
        "Status",
        "Submission date",
        "Total reported spend (£)",
        "Financial year",
        "Quarter",
    ];

    // Prefixed onto every row of every section file so each one stays filterable and joinable by organisation.
    private static readonly string[] OrganisationColumns = ["Organisation", RegionColumn, "ONS code", "E-code"];

    // BLUM-9106 (behind BLUM_9087): the last summary.csv column.
    private const string FormVersionsColumn = "Form versions";

    public async Task<Either<CollectionException, SubmissionsExportFile>> Handle(
        ExportSubmissionsCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<Either<CollectionException, SubmissionsExportFile>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            error => new CollectionForbiddenException(new CollectionId(request.CollectionId), error.Message));
    }

    private async Task<Either<CollectionException, SubmissionsExportFile>> HandleWithActor(
        ExportSubmissionsCommand request,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        var collectionId = new CollectionId(request.CollectionId);

        var lookup = await collectionQueries.GetByIdAsync(collectionId, includeDeleted: false, cancellationToken);
        if (lookup.IsNone)
        {
            return new CollectionDoesNotExistException(collectionId);
        }

        var collection = lookup.ValueUnsafe();

        // Quarterly-only: maturity assessments have no submission flow, so there is nothing to export for them. Guard
        // before the audit write so a rejected export logs no entry.
        if (!SubmissionEligibility.IsQuarterly(collection))
        {
            return new CollectionDoesNotExistException(collectionId);
        }

        var allOrganisations = await organisationQueries.GetOrganisations(cancellationToken);

        // ONE filter point, deliberately. Everything downstream reads from this list: the assignment resolution, the
        // reportable set that becomes summary.csv, AND the orgById lookup that decides which submitters contribute rows
        // to the four section files. Filtering only `reportable` would scope summary.csv and leave activity-results /
        // funding-spend / funding-summary / strategic-feedback carrying every region — the export would name one region
        // and contain them all. Ordinal to match Region.SupportedRegionNames, which is an Ordinal HashSet; the validator
        // has already rejected anything outside that set.
        var organisations = request.Region is { } filterRegion
            ? allOrganisations.Where(o => string.Equals(o.Region, filterRegion, StringComparison.Ordinal)).ToList()
            : allOrganisations;

        var submissions = await submissionQueries.GetSubmissionsForCollectionAsync(collection.Id, cancellationToken);
        var submittedAtByOrg = submissions.ToDictionary(s => s.OrganisationId, s => s.SubmittedAt);

        // Include every assigned org plus any org with a submission row even if no longer assigned, so its data is
        // never silently dropped from the export.
        var reportable = SubmissionAssignment.ResolveReportableOrganisations(collection, organisations, submittedAtByOrg.Keys);

        var spendByOrg = await spendEntryRepository.SumActualByOrganisationForCollectionAsync(collection.Id, cancellationToken);

        var financialYear = collection.FinancialYear ?? string.Empty;
        var quarter = collection.Quarter?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

        // One instant for the whole file, so every row is judged against the same moment.
        var utcNow = dateTimeProvider.GetUtcNow();

        // BLUM-9106: which version of each form this collection's reports were answered on. One value for the whole file:
        // the collection records the versions (BLUM-9103), so every report in it used the same ones.
        var formVersions = featureFlags.IsEnabled(Feature.BLUM_9087) ? await FormVersionsLabelAsync(collection, cancellationToken) : null;

        var rows = reportable
            .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .Select(o =>
            {
                var hasSubmission = submittedAtByOrg.TryGetValue(o.Id.Value, out var submittedAt);
                var status = SubmissionStatusRules.Derive(hasSubmission, submittedAt, collection.SubmissionDeadline, utcNow);
                var spend = spendByOrg.TryGetValue(o.Id.Value, out var total) ? total : 0m;
                return new[]
                {
                    o.Name,
                    o.Region,
                    SubmissionStatuses.Label(status),
                    submittedAt is { } at ? FormatDate(at) : string.Empty,
                    spend.ToString("0.00", CultureInfo.InvariantCulture),
                    financialYear,
                    quarter,
                };
            })
            .Select(row => formVersions is null ? row : [.. row, formVersions])
            .ToList<IReadOnlyList<string>>();

        var summaryCsv = CsvWriter.Build(formVersions is null ? Header : [.. Header, FormVersionsColumn], rows);

        // Decode each submitter's frozen snapshot once, ordered by name to match the summary. Non-submitters and legacy
        // submissions with no snapshot are absent here (they stay in summary.csv), so the section files never fabricate
        // empty rows for them.
        var orgById = organisations.ToDictionary(o => o.Id.Value);
        var submitters = new List<(Organisation Org, ReportSnapshotDocument Snapshot)>();
        foreach (var submission in submissions)
        {
            if (submission.ReportSnapshotJson is null || !orgById.TryGetValue(submission.OrganisationId, out var org))
            {
                continue;
            }

            // TryRead, not Deserialize: one corrupt or legacy-invalid snapshot must not fail the whole window ZIP.
            // That organisation is omitted from the section files and stays in summary.csv.
            var snapshot = ReportSnapshotJson.TryRead(submission.ReportSnapshotJson);
            if (snapshot is null)
            {
                continue;
            }

            submitters.Add((org, snapshot));
        }

        submitters.Sort((a, b) => string.Compare(a.Org.Name, b.Org.Name, StringComparison.OrdinalIgnoreCase));

        var sources = submitters
            .Select(x => new ReportSnapshotCsvSource(OrganisationCells(x.Org), x.Snapshot))
            .ToList();

        // Entry names are a downstream data contract for the analysts who consume the ZIP, pinned by tests.
        // BLUM-7716 renamed activity-key-results.csv to activity-results.csv to match the "Activity results"
        // wording BLUM-7563 introduced; the activity_key_results module code is a stored identifier and stays.
        var content = ZipWriter.Build(
        [
            ("summary.csv", summaryCsv),
            ("activity-results.csv", BuildSectionCsv("Activity", sources, s => s.ActivitySections)),
            ("funding-spend.csv", BuildFundingCsv(sources, financialYear, request.Region, featureFlags.IsEnabled(Feature.BLUM_9021))),
            ("funding-summary.csv", BuildFundingSummaryCsv(sources, financialYear)),
            ("strategic-feedback.csv", featureFlags.IsEnabled(Feature.BLUM_8329)
                ? BuildStrategicFeedbackCsv(submitters)
                : BuildSectionCsv("Section", sources, s => s.FeedbackSections)),
        ]);

        await auditLogRepository.AppendAsync(
            AuditLog.Create(
                actorId,
                AuditTargetType.Collection,
                collection.Id.Value,
                AuditAction.SubmissionExported,
                before: null,
                after: JsonSerializer.Serialize(new
                {
                    format = "zip",
                    financialYear = collection.FinancialYear,
                    quarter = collection.Quarter,
                    // Null for an all-regions export. organisationCount counts what the file actually covers, so on a
                    // regional export it is the in-region total, not the window's.
                    region = request.Region,
                    organisationCount = reportable.Count,
                })),
            cancellationToken);

        return new SubmissionsExportFile(BuildFileName(collection, request.Region), content);
    }

    // activity-results.csv / strategic-feedback.csv: the same organisation-prefixed shape over a submitter's
    // activity or feedback sections. Answers are already rendered in the frozen snapshot.
    private static byte[] BuildSectionCsv(
        string sectionColumnHeader,
        IReadOnlyList<ReportSnapshotCsvSource> sources,
        Func<ReportSnapshotDocument, IReadOnlyList<ReportSnapshotSection>> selectSections) =>
        CsvWriter.Build(
            [.. OrganisationColumns, sectionColumnHeader, "Question", "Answer"],
            ReportSnapshotCsv.SectionRows(sources, selectSections));

    // strategic-feedback.csv behind BLUM_8329 (one row per organisation instead of one per question): the name first
    // and the four questions next, as the LA's own file has them, then the rest of the identity columns so the file
    // still filters by region and joins to the other files in the ZIP. Submitters arrive name-sorted and
    // region-filtered; one with no strategic feedback in its report gets no row, as in the long file.
    private static byte[] BuildStrategicFeedbackCsv(IReadOnlyList<(Organisation Org, ReportSnapshotDocument Snapshot)> submitters)
    {
        var rows = new List<IReadOnlyList<string>>();
        foreach (var (org, snapshot) in submitters)
        {
            if (ReportSnapshotCsv.StrategicFeedbackAnswers(snapshot) is { } answers)
            {
                // The same identity cells as every other file, split around the answers.
                var identity = OrganisationCells(org);
                rows.Add([identity[0], .. answers, .. identity[1..]]);
            }
        }

        return CsvWriter.Build(
            ["Local authority", .. ReportSnapshotCsv.StrategicFeedbackQuestionColumns, .. OrganisationColumns[1..]],
            rows);
    }

    // funding-spend.csv: the per-activity funding table across all funding-bearing submitters, one rectangle.
    // financialYear is a single value for the whole export (one collection), threaded in rather than stored on the
    // per-organisation frozen snapshot.
    //
    // With BLUM-9021 on, the file closes with a grand total of every organisation's money — a third grain beside the
    // per-activity rows and per-organisation subtotals, told apart by the same Row type column. It belongs to no one
    // organisation, so its identity cells are blank except Region on a regional export, where the total is that
    // region's and the row should say so once the file is off the service.
    private static byte[] BuildFundingCsv(
        IReadOnlyList<ReportSnapshotCsvSource> sources,
        string financialYear,
        string? region,
        bool includeGrandTotal)
    {
        // The header is always the CURRENT shape, never "whichever snapshot happens to be first" — a window can
        // straddle a deploy that changed this shape (e.g. BLUM-7130), and Rows/Header are untyped string lists with
        // no schema version, so an old-shape row rendered under the new header (or vice versa) would silently
        // misalign every cell rather than fail loudly (a legacy row's "Total spent so far" landing under "Category"
        // with no error). Any source whose own frozen header doesn't match the current shape is treated like a
        // corrupt snapshot for THIS file — omitted rather than corrupting it; it still exports fine in its own
        // per-organisation ZIP and keeps its place in summary.csv / funding-summary.csv (Totals is separately
        // nullable there).
        var compatibleSources = sources
            .Where(s => s.Snapshot.Funding is null || s.Snapshot.Funding.Header.SequenceEqual(ReportSnapshotCsv.FundingHeader))
            .ToList();

        var rows = ReportSnapshotCsv.FundingRows(compatibleSources, financialYear);

        // Totalled over compatibleSources, the list the subtotals above came from, so the grand total is exactly the
        // sum of the subtotals in the file — a source dropped for its header shape is dropped from both. Its identity
        // cells are derived from OrganisationColumns rather than written out, so the row keeps the file's width and
        // the region keeps its column if those columns are ever changed.
        if (includeGrandTotal
            && ReportSnapshotCsv.FundingGrandTotalRow(compatibleSources, GrandTotalIdentityCells(region), financialYear) is { } grandTotal)
        {
            rows.Add(grandTotal);
        }

        // "Row type" closes the header because FundingRows closes every row with it: this file carries per-activity
        // rows AND a per-organisation subtotal (BLUM-7668) — and, with BLUM-9021, a grand total — and the column is
        // what keeps the grains separable when the file is pivoted or a money column is summed whole.
        return CsvWriter.Build(
            [.. OrganisationColumns, "Financial year", .. ReportSnapshotCsv.FundingHeader, ReportSnapshotCsv.RowTypeColumn],
            rows);
    }

    // "Enter activity results: version 1; Provide strategic feedback: version 2" — each form the collection records a
    // version for, in the forms' display order. Empty for a collection that records none (a maturity assessment). Each
    // form is named as its recorded version names it, so an archived form is named rather than shown as its id; it has
    // no live display order, so it comes last.
    private async Task<string> FormVersionsLabelAsync(Collection collection, CancellationToken cancellationToken)
    {
        var recorded = collection.Forms.Where(f => f.FormModuleVersionId is not null).ToList();
        if (recorded.Count == 0)
        {
            return string.Empty;
        }

        // A collection records a handful of forms, so one read per version is a handful of reads.
        var versions = new List<(FormModuleId ModuleId, string Name, int Number)>();
        foreach (var form in recorded)
        {
            var version = await formModuleVersionQueries.GetByIdAsync(form.FormModuleVersionId!, cancellationToken);
            if (version.IsSome)
            {
                var saved = version.ValueUnsafe();
                versions.Add((form.FormModuleId, FormModuleDefinition.Parse(saved.Definition).Name, saved.VersionNumber));
            }
        }

        var displayOrder = (await formModuleQueries.GetAllActiveAsync(cancellationToken)).ToDictionary(m => m.Id, m => m.DisplayOrder);

        return string.Join(
            "; ",
            versions
                .OrderBy(v => displayOrder.TryGetValue(v.ModuleId, out var order) ? order : int.MaxValue)
                .Select(v => $"{v.Name}: version {v.Number}"));
    }

    // funding-summary.csv: one wide row per organisation of the funding totals that sit below the funding table in
    // the LA's own export. Their grain is the organisation, not the activity, so they get their own file rather than
    // a sub-header inside funding-spend.csv — under one shared header their amounts landed in the per-activity money
    // columns.
    private static byte[] BuildFundingSummaryCsv(IReadOnlyList<ReportSnapshotCsvSource> sources, string financialYear) =>
        CsvWriter.Build(
            [.. OrganisationColumns, "Financial year", .. ReportSnapshotCsv.FundingSummaryTotalsColumns],
            ReportSnapshotCsv.FundingSummaryTotalsRows(sources, financialYear));

    private static string[] OrganisationCells(Organisation org) =>
        [org.Name, org.Region, org.OnsCode ?? string.Empty, org.ECode ?? string.Empty];

    // The grand total's organisation-identity cells: blank under every OrganisationColumns heading except Region on a
    // regional export (BLUM-9021).
    private static string[] GrandTotalIdentityCells(string? region) =>
        OrganisationColumns.Select(column => column == RegionColumn ? region ?? string.Empty : string.Empty).ToArray();

    // The submission date as an ISO calendar date — the time component is not needed for offline analysis.
    private static string FormatDate(DateTime submittedAt) =>
        DateTime.SpecifyKind(submittedAt, DateTimeKind.Utc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // A regional export names its region so the file still says what it holds once it is off the service:
    // "submissions-2026-2027-q1-yorkshire-and-the-humber.zip". All nine supported region names are letters and spaces
    // only, so lowercasing and hyphenating spaces is exact and needs no slug helper — and the validator has already
    // rejected anything that is not one of them. Absent region leaves the name exactly as it was.
    private static string BuildFileName(Collection collection, string? region)
    {
        var financialYear = (collection.FinancialYear ?? "window").Replace('/', '-').Replace(' ', '-');
        var quarter = collection.Quarter is { } q ? $"-q{q}" : string.Empty;
        var regionSegment = region is null ? string.Empty : $"-{region.ToLowerInvariant().Replace(' ', '-')}";
        return $"submissions-{financialYear}{quarter}{regionSegment}.zip";
    }
}