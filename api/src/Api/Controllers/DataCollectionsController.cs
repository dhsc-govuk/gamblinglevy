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
using Domain.Collections;
using Domain.ValueObjects;
using LanguageExt;
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
    IQuarterlyReportExportService exportService)
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
    [HttpGet("{collectionId:guid}/submissions/export")]
    public async Task<IActionResult> ExportSubmissions(
        [FromRoute] Guid collectionId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ExportSubmissionsCommand(collectionId), cancellationToken);
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
        var result = await exportService.GenerateForOrganisationAsync(collectionId, organisationId, cancellationToken);
        return result.Match<IActionResult>(
            file => File(file.Content, "application/zip", file.FileName),
            () => NotFound());
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
}