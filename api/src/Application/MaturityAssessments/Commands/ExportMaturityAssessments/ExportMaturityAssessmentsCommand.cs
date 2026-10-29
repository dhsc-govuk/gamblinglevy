using System.Globalization;
using System.Text.Json;
using Application.Common.Csv;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.MaturityAssessments.Exceptions;
using Application.MaturityAssessments.Export;
using Application.MaturityAssessments.Protocol;
using Application.MaturityAssessments.Snapshots;
using Application.Submissions;
using Domain.AuditLogs;
using Domain.Collections;
using Domain.MaturityAssessments;
using Domain.Organisations;
using Domain.Users;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;

namespace Application.MaturityAssessments.Commands.ExportMaturityAssessments;

// DHSC consolidated maturity-assessment export (BLUM-7859 AC4): one ZIP for a maturity-assessment window holding
// summary.csv (every reportable organisation with its derived status) and maturity-assessment.csv (one WIDE row per
// submitting organisation, one column per question). A command rather than a controller-service read because it
// is audited, like ExportSubmissionsCommand. Region narrows both files, as the quarterly export does.
public record ExportMaturityAssessmentsCommand(Guid CollectionId, string? Region = null)
    : IRequest<Either<MaturityAssessmentException, MaturityAssessmentsExportFile>>;

public record MaturityAssessmentsExportFile(string FileName, byte[] Content);

public class ExportMaturityAssessmentsCommandHandler(
    ICollectionQueries collectionQueries,
    IMaturityAssessmentQueries assessmentQueries,
    IOrganisationQueries organisationQueries,
    IUserQueries userQueries,
    IAuditLogRepository auditLogRepository,
    PermissionsService permissionsService,
    IDateTimeProvider dateTimeProvider)
    : IRequestHandler<ExportMaturityAssessmentsCommand, Either<MaturityAssessmentException, MaturityAssessmentsExportFile>>
{
    private static readonly string[] SummaryHeader =
    [
        "Organisation",
        "Region",
        "ONS code",
        "E-code",
        "Status",
        "Reference",
        "Submitted by",
        "Submitted on",
        "Financial year",
    ];

    private static readonly string[] OrganisationColumns = ["Organisation", "Region", "ONS code", "E-code"];

    public async Task<Either<MaturityAssessmentException, MaturityAssessmentsExportFile>> Handle(
        ExportMaturityAssessmentsCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<Either<MaturityAssessmentException, MaturityAssessmentsExportFile>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            error => new MaturityAssessmentForbiddenException(MaturityAssessmentId.Empty(), error.Message));
    }

    private async Task<Either<MaturityAssessmentException, MaturityAssessmentsExportFile>> HandleWithActor(
        ExportMaturityAssessmentsCommand request,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        var collectionId = new CollectionId(request.CollectionId);

        var lookup = await collectionQueries.GetByIdAsync(collectionId, includeDeleted: false, cancellationToken);
        if (lookup.IsNone)
        {
            return new MaturityAssessmentCollectionNotEligibleException(collectionId);
        }

        var collection = lookup.ValueUnsafe();

        // Guarded before the audit write so a rejected export logs no entry.
        if (!SubmissionEligibility.IsMaturityAssessment(collection))
        {
            return new MaturityAssessmentCollectionNotEligibleException(collectionId);
        }

        var allOrganisations = await organisationQueries.GetOrganisations(cancellationToken);

        // ONE filter point: everything downstream reads this list (the ExportSubmissionsCommand lesson).
        var organisations = request.Region is { } filterRegion
            ? allOrganisations.Where(o => string.Equals(o.Region, filterRegion, StringComparison.Ordinal)).ToList()
            : allOrganisations;

        var statusRows = await assessmentQueries.GetStatusRowsForCollectionAsync(collection.Id, cancellationToken);
        var submittedAtByOrg = statusRows.ToDictionary(r => r.OrganisationId, r => r.SubmittedAt);
        var reportable = SubmissionAssignment.ResolveReportableOrganisations(collection, organisations, submittedAtByOrg.Keys);

        var submitted = await assessmentQueries.GetSubmittedForCollectionAsync(collection.Id, cancellationToken);
        var submittedByOrg = submitted.ToDictionary(r => r.OrganisationId);

        var users = await userQueries.GetAllUsers(cancellationToken);
        var userNames = users.ToDictionary(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim());

        var financialYear = collection.FinancialYear ?? string.Empty;

        // One instant for the whole file, so every row is judged against the same moment.
        var utcNow = dateTimeProvider.GetUtcNow();

        var summaryRows = reportable
            .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .Select(o =>
            {
                var hasRow = submittedAtByOrg.TryGetValue(o.Id.Value, out var submittedAt);
                var status = SubmissionStatusRules.Derive(hasRow, submittedAt, collection.SubmissionDeadline, utcNow);
                submittedByOrg.TryGetValue(o.Id.Value, out var row);
                var submittedBy = row.SubmittedBy is { } by && userNames.TryGetValue(by, out var name) ? name : string.Empty;
                return new[]
                {
                    o.Name,
                    o.Region,
                    o.OnsCode ?? string.Empty,
                    o.ECode ?? string.Empty,
                    SubmissionStatuses.Label(status),
                    row.ReferenceNumber ?? string.Empty,
                    submittedBy,
                    submittedAt is { } at ? FormatDate(at) : string.Empty,
                    financialYear,
                };
            })
            .ToList<IReadOnlyList<string>>();

        // Decode each submitter's frozen snapshot once, in name order. TryRead: one corrupt snapshot must not fail
        // the whole window ZIP — that organisation stays in summary.csv and is absent from the wide file.
        var orgById = organisations.ToDictionary(o => o.Id.Value);
        var submitters = new List<(Organisation Org, MaturityAssessmentSnapshotDocument Snapshot)>();
        foreach (var row in submitted)
        {
            if (!orgById.TryGetValue(row.OrganisationId, out var org))
            {
                continue;
            }

            var snapshot = MaturityAssessmentSnapshotJson.TryRead(row.SnapshotJson);
            if (snapshot is null)
            {
                continue;
            }

            submitters.Add((org, snapshot));
        }

        submitters.Sort((a, b) => string.Compare(a.Org.Name, b.Org.Name, StringComparison.OrdinalIgnoreCase));

        // The window's current forms PLUS every section a submitter's frozen snapshot holds (BLUM-8093). The forms can
        // change while the window is open, so the current set alone would drop answers an LA submitted for a section
        // the admin unticked afterwards; a form nobody has and nobody submitted still gets no always-blank columns.
        var sectionCodes = MaturityCollectionForms.SectionCodesFor(collection)
            .Concat(submitters.SelectMany(s => s.Snapshot.Sections.Select(section => section.Code)))
            .ToHashSet(StringComparer.Ordinal);
        var columns = MaturityAssessmentCsv.WideColumns(
            MaturityAssessmentCsv.MaxRoles(submitters.Select(s => s.Snapshot)),
            sectionCodes);
        var wideRows = submitters
            .Select(s => MaturityAssessmentCsv.WideRow(OrganisationCells(s.Org), s.Snapshot, columns))
            .ToList();

        var content = ZipWriter.Build(
        [
            ("summary.csv", CsvWriter.Build(SummaryHeader, summaryRows)),
            ("maturity-assessment.csv", CsvWriter.Build(MaturityAssessmentCsv.WideHeader(OrganisationColumns, columns), wideRows)),
        ]);

        await auditLogRepository.AppendAsync(
            AuditLog.Create(
                actorId,
                AuditTargetType.Collection,
                collection.Id.Value,
                AuditAction.MaturityAssessmentExported,
                before: null,
                after: JsonSerializer.Serialize(new
                {
                    format = "zip",
                    financialYear = collection.FinancialYear,
                    region = request.Region,
                    organisationCount = reportable.Count,
                    submittedCount = submitters.Count,
                })),
            cancellationToken);

        return new MaturityAssessmentsExportFile(BuildFileName(collection, request.Region), content);
    }

    private static string[] OrganisationCells(Organisation org) =>
        [org.Name, org.Region, org.OnsCode ?? string.Empty, org.ECode ?? string.Empty];

    private static string FormatDate(DateTime submittedAt) =>
        DateTime.SpecifyKind(submittedAt, DateTimeKind.Utc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // "maturity-assessments-2026-27.zip", "-north-west" appended for a regional export (the nine supported region
    // names are letters and spaces only, so lowercasing + hyphenating is exact).
    private static string BuildFileName(Collection collection, string? region)
    {
        var financialYear = (collection.FinancialYear ?? "window").Replace('/', '-').Replace(' ', '-');
        var regionSegment = region is null ? string.Empty : $"-{region.ToLowerInvariant().Replace(' ', '-')}";
        return $"maturity-assessments-{financialYear}{regionSegment}.zip";
    }
}