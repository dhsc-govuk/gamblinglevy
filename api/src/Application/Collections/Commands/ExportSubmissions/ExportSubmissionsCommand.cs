using System.Globalization;
using System.Text.Json;
using Application.Collections.Exceptions;
using Application.Common.Csv;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Common.ReportSnapshots;
using Application.Submissions;
using Domain.AuditLogs;
using Domain.Collections;
using Domain.Organisations;
using Domain.Users;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;

namespace Application.Collections.Commands.ExportSubmissions;

// DHSC submission export (BLUM-6100): a ZIP of per-section CSVs for a reporting window, aggregated across every
// organisation. summary.csv keeps the flat per-organisation summary (name, status, submitted date, total reported
// spend, financial year, quarter) covering submitters and non-submitters alike; activity-key-results.csv,
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
public record ExportSubmissionsCommand(Guid CollectionId) : IRequest<Either<CollectionException, SubmissionsExportFile>>;

public record SubmissionsExportFile(string FileName, byte[] Content);

public class ExportSubmissionsCommandHandler(
    ICollectionQueries collectionQueries,
    ISubmissionQueries submissionQueries,
    IOrganisationQueries organisationQueries,
    ISubmissionSpendEntryRepository spendEntryRepository,
    IAuditLogRepository auditLogRepository,
    PermissionsService permissionsService)
    : IRequestHandler<ExportSubmissionsCommand, Either<CollectionException, SubmissionsExportFile>>
{
    private static readonly string[] Header =
    [
        "Organisation",
        "Status",
        "Submission date",
        "Total reported spend (£)",
        "Financial year",
        "Quarter",
    ];

    // Prefixed onto every row of every section file so each one stays filterable and joinable by organisation.
    private static readonly string[] OrganisationColumns = ["Organisation", "ONS code", "E-code"];

    // Header for a window in which nobody has recorded funding yet, so there is no frozen table to take columns from.
    private static readonly string[] EmptyFundingColumns = ["Activity", "Levy spent this quarter (£)", "Total spent so far (£)"];

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

        var submissions = await submissionQueries.GetSubmissionsForCollectionAsync(collection.Id, cancellationToken);
        var submittedAtByOrg = submissions.ToDictionary(s => s.OrganisationId, s => s.SubmittedAt);

        // Include every assigned org plus any org with a submission row even if no longer assigned, so its data is
        // never silently dropped from the export.
        var reportable = SubmissionAssignment.ResolveReportableOrganisations(collection, allOrganisations, submittedAtByOrg.Keys);

        var spendByOrg = await spendEntryRepository.SumActualByOrganisationForCollectionAsync(collection.Id, cancellationToken);

        var financialYear = collection.FinancialYear ?? string.Empty;
        var quarter = collection.Quarter?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

        var rows = reportable
            .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .Select(o =>
            {
                var hasSubmission = submittedAtByOrg.TryGetValue(o.Id.Value, out var submittedAt);
                var status = SubmissionStatusRules.Derive(hasSubmission, submittedAt, collection.SubmissionDeadline);
                var spend = spendByOrg.TryGetValue(o.Id.Value, out var total) ? total : 0m;
                return new[]
                {
                    o.Name,
                    SubmissionStatuses.Label(status),
                    submittedAt is { } at ? FormatDate(at) : string.Empty,
                    spend.ToString("0.00", CultureInfo.InvariantCulture),
                    financialYear,
                    quarter,
                };
            })
            .ToList<IReadOnlyList<string>>();

        var summaryCsv = CsvWriter.Build(Header, rows);

        // Decode each submitter's frozen snapshot once, ordered by name to match the summary. Non-submitters and legacy
        // submissions with no snapshot are absent here (they stay in summary.csv), so the section files never fabricate
        // empty rows for them.
        var orgById = allOrganisations.ToDictionary(o => o.Id.Value);
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

        var content = ZipWriter.Build(
        [
            ("summary.csv", summaryCsv),
            ("activity-key-results.csv", BuildSectionCsv("Activity", sources, s => s.ActivitySections)),
            ("funding-spend.csv", BuildFundingCsv(sources)),
            ("funding-summary.csv", BuildFundingSummaryCsv(sources)),
            ("strategic-feedback.csv", BuildSectionCsv("Section", sources, s => s.FeedbackSections)),
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
                    organisationCount = reportable.Count,
                })),
            cancellationToken);

        return new SubmissionsExportFile(BuildFileName(collection), content);
    }

    // activity-key-results.csv / strategic-feedback.csv: the same organisation-prefixed shape over a submitter's
    // activity or feedback sections. Answers are already rendered in the frozen snapshot.
    private static byte[] BuildSectionCsv(
        string sectionColumnHeader,
        IReadOnlyList<ReportSnapshotCsvSource> sources,
        Func<ReportSnapshotDocument, IReadOnlyList<ReportSnapshotSection>> selectSections) =>
        CsvWriter.Build(
            [.. OrganisationColumns, sectionColumnHeader, "Question", "Answer"],
            ReportSnapshotCsv.SectionRows(sources, selectSections));

    // funding-spend.csv: the per-activity funding table across all funding-bearing submitters, one rectangle.
    private static byte[] BuildFundingCsv(IReadOnlyList<ReportSnapshotCsvSource> sources)
    {
        // The dynamic Forecast Q{n} columns depend only on the collection quarter, so they are identical across orgs in
        // one window — take the header from the first funding-bearing submitter.
        var fundingHeader = sources
            .Select(x => x.Snapshot.Funding)
            .FirstOrDefault(funding => funding is not null)?.Header ?? EmptyFundingColumns;

        return CsvWriter.Build([.. OrganisationColumns, .. fundingHeader], ReportSnapshotCsv.FundingRows(sources));
    }

    // funding-summary.csv: the per-organisation totals that sit below the funding table in the LA's own export. Their
    // grain is the organisation, not the activity, so they get their own file rather than a sub-header inside
    // funding-spend.csv — under one shared header their amounts landed in the per-activity money columns.
    private static byte[] BuildFundingSummaryCsv(IReadOnlyList<ReportSnapshotCsvSource> sources) =>
        CsvWriter.Build(
            [.. OrganisationColumns, .. ReportSnapshotCsv.FundingSummaryColumns],
            ReportSnapshotCsv.FundingSummaryRows(sources));

    private static string[] OrganisationCells(Organisation org) =>
        [org.Name, org.OnsCode ?? string.Empty, org.ECode ?? string.Empty];

    // The submission date as an ISO calendar date — the time component is not needed for offline analysis.
    private static string FormatDate(DateTime submittedAt) =>
        DateTime.SpecifyKind(submittedAt, DateTimeKind.Utc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string BuildFileName(Collection collection)
    {
        var financialYear = (collection.FinancialYear ?? "window").Replace('/', '-').Replace(' ', '-');
        var quarter = collection.Quarter is { } q ? $"-q{q}" : string.Empty;
        return $"submissions-{financialYear}{quarter}.zip";
    }
}