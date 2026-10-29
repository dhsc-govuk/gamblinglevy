using Api.Dtos;
using Api.Modules.Errors;
using Api.Services.Abstract;
using Application.Collections.Commands;
using Application.Collections.Commands.CloseCollection;
using Application.Collections.Commands.CreateCollection;
using Application.Collections.Commands.ExportSubmissions;
using Application.Collections.Commands.RestoreCollection;
using Application.Collections.Commands.RevertToDraftCollection;
using Application.Collections.Commands.SendManualReminder;
using Application.Collections.Commands.SoftDeleteCollection;
using Application.Collections.Commands.UpdateCollection;
using Application.Collections.Exceptions;
using Application.Common.Interfaces.Queries;
using Application.Common.Permissions;
using Application.MaturityAssessments.Commands.ExportMaturityAssessments;
using Application.Submissions;
using Application.Submissions.Commands.RegenerateReportSnapshot;
using Domain.Collections;
using Domain.Organisations;
using Domain.Users;
using Domain.ValueObjects;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[Route("data-collections")]
[ApiController]
[Authorize(Roles = Role.Admin)]
public class DataCollectionsController(
    ISender sender,
    IDataCollectionsControllerService controllerService,
    IDhscSubmissionsControllerService submissionsService,
    IQuarterlyReportExportService exportService,
    IMaturityAssessmentExportService maturityExportService,
    IReportSnapshotService reportSnapshotService,
    ICollectionQueries collectionQueries,
    ISubmissionQueries submissionQueries,
    PermissionsService permissionsService)
    : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CollectionSummaryDto>>> List(CancellationToken cancellationToken)
    {
        return Ok(await controllerService.ListAsync(cancellationToken));
    }

    [HttpGet("deleted")]
    public async Task<ActionResult<IReadOnlyList<CollectionSummaryDto>>> Deleted(CancellationToken cancellationToken)
    {
        return Ok(await controllerService.DeletedAsync(cancellationToken));
    }

    [HttpGet("form-options")]
    public async Task<ActionResult<IReadOnlyList<CollectionFormOptionDto>>> FormOptions(CancellationToken cancellationToken)
    {
        return Ok(await controllerService.GetFormOptionsAsync(cancellationToken));
    }

    [HttpGet("reporting-las")]
    public async Task<ActionResult<IReadOnlyList<ReportingLaDto>>> ReportingLas(CancellationToken cancellationToken)
    {
        return Ok(await controllerService.GetReportingLasAsync(cancellationToken));
    }

    [HttpGet("{collectionId:guid}")]
    public async Task<ActionResult<CollectionDto>> Get(
        [FromRoute] Guid collectionId,
        CancellationToken cancellationToken)
    {
        var result = await controllerService.GetAsync(collectionId, cancellationToken);
        return result.Match<ActionResult<CollectionDto>>(dto => dto, () => NotFound());
    }

    // DHSC submission visibility (BLUM-6095): the per-organisation submission-status list for a reporting window.
    [HttpGet("{collectionId:guid}/submissions")]
    public async Task<ActionResult<CollectionSubmissionSummaryDto>> Submissions(
        [FromRoute] Guid collectionId,
        CancellationToken cancellationToken)
    {
        var result = await submissionsService.GetOrgStatusesAsync(collectionId, cancellationToken);
        return result.Match<ActionResult<CollectionSubmissionSummaryDto>>(dto => dto, () => NotFound());
    }

    // DHSC submission detail (BLUM-6445): one organisation's submission against a reporting window.
    [HttpGet("{collectionId:guid}/submissions/{organisationId:guid}")]
    public async Task<ActionResult<OrgSubmissionDetailDto>> SubmissionDetail(
        [FromRoute] Guid collectionId,
        [FromRoute] Guid organisationId,
        CancellationToken cancellationToken)
    {
        var result = await submissionsService.GetOrgDetailAsync(collectionId, organisationId, cancellationToken);
        return result.Match<ActionResult<OrgSubmissionDetailDto>>(dto => dto, () => NotFound());
    }

    // DHSC submission export (BLUM-6100): a ZIP of per-section CSVs (summary + the three report sections) across all
    // orgs in the window. Audited inside the command (AC5). "export" cannot collide with the {organisationId:guid} route.
    //
    // ?region= narrows it to one English region (BLUM-7682). The route TEMPLATE is unchanged, so this endpoint keeps
    // its RateLimitPolicy.Export bucket and its place in the authorisation/rate-limit coverage tests.
    //
    // The same URL serves a maturity-assessment window (BLUM-7859 AC4): one wide CSV of every submitted assessment
    // plus a summary, produced by its own audited command. Branching on the collection's type here keeps one download
    // link on the DHSC screens for both flows.
    [HttpGet("{collectionId:guid}/submissions/export")]
    public async Task<IActionResult> ExportSubmissions(
        [FromRoute] Guid collectionId,
        [FromQuery] string? region,
        CancellationToken cancellationToken)
    {
        // An empty or whitespace region is the "All regions" option, not an unsupported one. Normalising here lets the
        // validator treat any non-null value as a deliberate choice and reject it strictly.
        var normalisedRegion = string.IsNullOrWhiteSpace(region) ? null : region;

        if (await IsMaturityAssessmentAsync(collectionId, cancellationToken))
        {
            var assessmentsResult = await sender.Send(new ExportMaturityAssessmentsCommand(collectionId, normalisedRegion), cancellationToken);
            return assessmentsResult.Match<IActionResult>(
                file => File(file.Content, "application/zip", file.FileName),
                error => error.ToObjectResult());
        }

        var result = await sender.Send(new ExportSubmissionsCommand(collectionId, normalisedRegion), cancellationToken);
        return result.Match<IActionResult>(
            file => File(file.Content, "application/zip", file.FileName),
            error => error.ToObjectResult());
    }

    // DHSC per-organisation report download (BLUM-6100): the individual organisation's submitted quarterly report ZIP,
    // reusing the export service that powers the LA-facing download. NotFound when the org is not assigned or has not
    // submitted.
    [HttpGet("{collectionId:guid}/submissions/{organisationId:guid}/export")]
    public async Task<IActionResult> ExportOrgReport(
        [FromRoute] Guid collectionId,
        [FromRoute] Guid organisationId,
        CancellationToken cancellationToken)
    {
        var result = await IsMaturityAssessmentAsync(collectionId, cancellationToken)
            ? await maturityExportService.GenerateForOrganisationAsync(collectionId, organisationId, cancellationToken)
            : await exportService.GenerateForOrganisationAsync(collectionId, organisationId, cancellationToken);
        return result.Match<IActionResult>(
            file => File(file.Content, "application/zip", file.FileName),
            () => NotFound());
    }

    // One-off admin repair (BLUM-7130 CSV review): re-freezes every already-submitted submission's report
    // snapshot in this collection under today's rules and header shape. Fixes a collection whose DHSC-wide
    // export silently drops funding-spend.csv/funding-summary.csv rows for organisations that submitted before
    // a header-shape change — see ExportSubmissionsCommand.BuildFundingCsv's compatibleSources guard, which
    // omits rather than risks misaligning cells. Safe to re-run: it only ever overwrites a submission's own
    // snapshot with a freshly-built one, and never touches a draft (unsubmitted submissions are skipped).
    [HttpPost("{collectionId:guid}/submissions/regenerate-snapshots")]
    public async Task<ActionResult<RegenerateReportSnapshotsResultDto>> RegenerateSnapshots(
        [FromRoute] Guid collectionId,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<ActionResult<RegenerateReportSnapshotsResultDto>>(
            async actorId =>
            {
                var collectionLookup = await collectionQueries.GetByIdAsync(new CollectionId(collectionId), cancellationToken: cancellationToken);
                if (collectionLookup.IsNone)
                {
                    return NotFound();
                }

                if (!SubmissionEligibility.IsQuarterly(collectionLookup.ValueUnsafe()))
                {
                    return NotFound();
                }

                var submissions = await submissionQueries.GetSubmissionsForCollectionAsync(new CollectionId(collectionId), cancellationToken);
                var submitted = submissions.Where(s => s.SubmittedAt is not null).ToList();

                var regenerated = 0;
                var skipped = new List<RegenerateReportSnapshotsSkippedDto>();

                foreach (var row in submitted)
                {
                    // A synthetic org-scoped permission, not the caller's own: this admin is rebuilding every
                    // submitting organisation's snapshot in turn, not reading their own. Lead is an arbitrary
                    // choice of a real LA role — the read paths this feeds (GetReportableActivitiesAsync,
                    // GetFundingSpendAsync, GetStrategicFeedbackAsync) scope by OrganisationId/UserId only, never
                    // by role.
                    var permission = Permission.OrganisationPermission(UserRole.Lead, new OrganisationId(row.OrganisationId), actorId);
                    var snapshotLookup = await reportSnapshotService.BuildAsync(permission, collectionId, cancellationToken);
                    if (snapshotLookup.IsNone)
                    {
                        skipped.Add(new RegenerateReportSnapshotsSkippedDto(row.OrganisationId, "No reportable content to snapshot."));
                        continue;
                    }

                    var json = reportSnapshotService.Serialize(snapshotLookup.ValueUnsafe());
                    var result = await sender.Send(
                        new RegenerateReportSnapshotCommand(collectionId, row.OrganisationId, json),
                        cancellationToken);

                    if (result.IsRight)
                    {
                        regenerated++;
                    }
                    else
                    {
                        result.IfLeft(error => skipped.Add(new RegenerateReportSnapshotsSkippedDto(row.OrganisationId, error.Message)));
                    }
                }

                return Ok(new RegenerateReportSnapshotsResultDto(regenerated, skipped));
            },
            error => Task.FromResult<ActionResult<RegenerateReportSnapshotsResultDto>>(Forbid()));
    }

    [HttpPost]
    public async Task<ActionResult<CollectionDto>> Create(
        [FromBody] CreateCollectionRequestDto request,
        CancellationToken cancellationToken)
    {
        var command = new CreateCollectionCommand
        {
            Name = request.Name,
            Purpose = request.Purpose,
            Type = request.Type,
            FinancialYear = request.FinancialYear,
            Quarter = request.Quarter,
            WindowSameAsReportingPeriod = request.WindowSameAsReportingPeriod,
            OpenDate = request.OpenDate,
            Deadline = request.Deadline,
            IsSubmittedByAllLas = request.IsSubmittedByAllLas,
            LocalAuthorityIds = request.LocalAuthorityIds ?? [],
            FormModuleIds = request.FormModuleIds ?? [],
            Reminders = MapReminders(request.Reminders),
            SaveAsDraft = request.SaveAsDraft,
        };

        var result = await sender.Send(command, cancellationToken);
        return await ToActionResult(result, cancellationToken);
    }

    [HttpPut("{collectionId:guid}")]
    public async Task<ActionResult<CollectionDto>> Update(
        [FromRoute] Guid collectionId,
        [FromBody] UpdateCollectionRequestDto request,
        CancellationToken cancellationToken)
    {
        var command = new UpdateCollectionCommand
        {
            Id = collectionId,
            Name = request.Name,
            Purpose = request.Purpose,
            Type = request.Type,
            FinancialYear = request.FinancialYear,
            Quarter = request.Quarter,
            WindowSameAsReportingPeriod = request.WindowSameAsReportingPeriod,
            CollectionOpenDate = request.CollectionOpenDate,
            SubmissionDeadline = request.SubmissionDeadline,
            IsSubmittedByAllLas = request.IsSubmittedByAllLas,
            FormModuleIds = request.FormModuleIds ?? [],
            LocalAuthorityIds = request.LocalAuthorityIds ?? [],
            Reminders = MapReminders(request.Reminders),
            SaveAsDraft = request.SaveAsDraft,
        };

        var result = await sender.Send(command, cancellationToken);
        return await ToActionResult(result, cancellationToken);
    }

    [HttpPost("{collectionId:guid}/revert-to-draft")]
    public async Task<ActionResult<CollectionDto>> RevertToDraft(
        [FromRoute] Guid collectionId,
        CancellationToken cancellationToken)
    {
        var command = new RevertToDraftCollectionCommand { Id = collectionId };
        var result = await sender.Send(command, cancellationToken);
        return await ToActionResult(result, cancellationToken);
    }

    [HttpPost("{collectionId:guid}/close")]
    public async Task<ActionResult<CollectionDto>> Close(
        [FromRoute] Guid collectionId,
        CancellationToken cancellationToken)
    {
        var command = new CloseCollectionCommand { Id = collectionId };
        var result = await sender.Send(command, cancellationToken);
        return await ToActionResult(result, cancellationToken);
    }

    [HttpPost("{collectionId:guid}/send-reminder")]
    public async Task<ActionResult<CollectionDto>> SendReminder(
        [FromRoute] Guid collectionId,
        CancellationToken cancellationToken)
    {
        var command = new SendManualReminderCommand { Id = collectionId };
        var result = await sender.Send(command, cancellationToken);
        return await ToActionResult(result, cancellationToken);
    }

    [HttpPost("{collectionId:guid}/restore")]
    public async Task<ActionResult<CollectionDto>> Restore(
        [FromRoute] Guid collectionId,
        CancellationToken cancellationToken)
    {
        var command = new RestoreCollectionCommand { Id = collectionId };
        var result = await sender.Send(command, cancellationToken);
        return await ToActionResult(result, cancellationToken);
    }

    [HttpDelete("{collectionId:guid}")]
    public async Task<ActionResult<CollectionDto>> Delete(
        [FromRoute] Guid collectionId,
        CancellationToken cancellationToken)
    {
        var command = new SoftDeleteCollectionCommand { Id = collectionId };
        var result = await sender.Send(command, cancellationToken);
        return await ToActionResult(result, cancellationToken);
    }

    private async Task<ActionResult<CollectionDto>> ToActionResult(
        Either<CollectionException, Collection> result,
        CancellationToken cancellationToken)
    {
        return await result.MatchAsync<ActionResult<CollectionDto>>(
            async collection => await controllerService.ToDtoAsync(collection, cancellationToken),
            error => error.ToObjectResult());
    }

    private static IReadOnlyList<ReminderInput> MapReminders(IReadOnlyList<ReminderRequestDto>? reminders) =>
        reminders?.Select(r => new ReminderInput(r.OffsetType, r.Days)).ToList() ?? [];

    // The export URLs are shared by both submission flows; an unknown or deleted collection falls to the quarterly path,
    // whose own lookup answers 404.
    private async Task<bool> IsMaturityAssessmentAsync(Guid collectionId, CancellationToken cancellationToken)
    {
        var lookup = await collectionQueries.GetByIdAsync(new CollectionId(collectionId), includeDeleted: false, cancellationToken);
        return lookup.IsSome && SubmissionEligibility.IsMaturityAssessment(lookup.ValueUnsafe());
    }
}