using Api.Common;
using Api.Dtos;
using Api.Modules.Errors;
using Api.Services.Abstract;
using Application.Common.Permissions;
using Application.Common.Telemetry;
using Application.MaturityAssessments.Commands.CompleteMaturitySection;
using Application.MaturityAssessments.Commands.SaveMaturityAnswers;
using Application.MaturityAssessments.Commands.StartMaturityAssessment;
using Application.MaturityAssessments.Commands.SubmitMaturityAssessment;
using Domain.ValueObjects;
using LanguageExt.UnsafeValueAccess;
using MediatR;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

// LA-facing annual maturity assessment (BLUM-7855 / 7856 / 7858 / 7859): the "Manage your maturity assessment"
// context and task list, the per-section pages, check-your-answers completion, submit, and the read-only
// submitted copy. Both LA roles may read and submit, as for the quarterly report. Every route is collection-scoped
// by URL (the BLUM-6950 rule) and org-scoped by the caller's Permission — never by an id in the request.
[Route("maturity-assessment")]
[ApiController]
[Authorize(Roles = Role.AnyOrganisationMember)]
public class MaturityAssessmentController(
    PermissionsService permissionsService,
    ISender sender,
    IMaturityAssessmentControllerService controllerService,
    IMaturityAssessmentSnapshotService snapshotService,
    IMaturityAssessmentExportService exportService,
    TelemetryClient telemetryClient)
    : ControllerBase
{
    // 404 when the collection does not exist, is a draft, is not a maturity assessment, or is not assigned to the
    // caller's organisation (anti-probing). A closed window still answers — a submitted assessment stays viewable.
    [HttpGet("{collectionId:guid}/context")]
    public async Task<ActionResult<MaturityAssessmentContextDto>> Context(
        [FromRoute] Guid collectionId,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetOrganisationPermissions()
            .MatchAsync<ActionResult<MaturityAssessmentContextDto>>(
                async permission =>
                {
                    var result = await controllerService.GetContextAsync(permission, collectionId, cancellationToken);
                    return result.Match<ActionResult<MaturityAssessmentContextDto>>(dto => dto, () => NotFound());
                },
                error => StatusCode(StatusCodes.Status403Forbidden, error.Message));
    }

    // Starts the assessment (pre-filling it). 409 when already started, 422 REPORT_NOT_OPEN outside the window.
    [HttpPost("{collectionId:guid}/assessment")]
    public async Task<ActionResult<MaturityTaskListDto>> Start(
        [FromRoute] Guid collectionId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new StartMaturityAssessmentCommand { CollectionId = collectionId }, cancellationToken);
        return await result.MatchAsync<ActionResult<MaturityTaskListDto>>(
            async assessment =>
            {
                telemetryClient.TrackEventIfEnabled(
                    "maturity_assessment_started",
                    new Dictionary<string, string> { ["collectionId"] = collectionId.ToString() });
                return await controllerService.ToTaskListDtoAsync(assessment, cancellationToken);
            },
            error => error.ToObjectResult());
    }

    [HttpGet("{collectionId:guid}/task-list")]
    public async Task<ActionResult<MaturityTaskListDto>> TaskList(
        [FromRoute] Guid collectionId,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetOrganisationPermissions()
            .MatchAsync<ActionResult<MaturityTaskListDto>>(
                async permission =>
                {
                    var result = await controllerService.GetTaskListAsync(permission, collectionId, cancellationToken);
                    return result.Match<ActionResult<MaturityTaskListDto>>(dto => dto, () => NotFound());
                },
                error => StatusCode(StatusCodes.Status403Forbidden, error.Message));
    }

    [HttpGet("{collectionId:guid}/sections/{sectionCode}")]
    public async Task<ActionResult<MaturitySectionDto>> Section(
        [FromRoute] Guid collectionId,
        [FromRoute] string sectionCode,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetOrganisationPermissions()
            .MatchAsync<ActionResult<MaturitySectionDto>>(
                async permission =>
                {
                    var result = await controllerService.GetSectionAsync(permission, collectionId, sectionCode, cancellationToken);
                    return result.Match<ActionResult<MaturitySectionDto>>(dto => dto, () => NotFound());
                },
                error => StatusCode(StatusCodes.Status403Forbidden, error.Message));
    }

    // Saves one page of a section — only the submitted question codes are replaced (per-page merge). SaveAsDraft =
    // "Save and come back later". Never completes the section; that is the complete endpoint below.
    [HttpPut("{collectionId:guid}/sections/{sectionCode}/answers")]
    [AcceptsEncodedJsonBody]
    public async Task<ActionResult<MaturitySectionStateDto>> SaveAnswers(
        [FromRoute] Guid collectionId,
        [FromRoute] string sectionCode,
        [FromBody] SaveMaturityAnswersRequestDto request,
        CancellationToken cancellationToken)
    {
        var command = new SaveMaturityAnswersCommand
        {
            CollectionId = collectionId,
            SectionCode = sectionCode,
            PageCode = request.PageCode,
            SaveAsDraft = request.SaveAsDraft,
            Answers = request.Answers?.Select(a => new MaturityAnswerInput(a.QuestionCode, a.Value)).ToList() ?? [],
        };

        var result = await sender.Send(command, cancellationToken);
        return result.Match<ActionResult<MaturitySectionStateDto>>(
            status => new MaturitySectionStateDto(sectionCode, status.ToString()),
            error => error.ToObjectResult());
    }

    // The section's check-your-answers Continue: no body — the saved answers are validated.
    [HttpPost("{collectionId:guid}/sections/{sectionCode}/complete")]
    public async Task<ActionResult<MaturitySectionStateDto>> CompleteSection(
        [FromRoute] Guid collectionId,
        [FromRoute] string sectionCode,
        CancellationToken cancellationToken)
    {
        var command = new CompleteMaturitySectionCommand { CollectionId = collectionId, SectionCode = sectionCode };
        var result = await sender.Send(command, cancellationToken);
        return result.Match<ActionResult<MaturitySectionStateDto>>(
            status => new MaturitySectionStateDto(sectionCode, status.ToString()),
            error => error.ToObjectResult());
    }

    // "Submit your assessment" — irreversible. 422 REPORT_INCOMPLETE when a section is still incomplete, 409 when
    // already submitted.
    [HttpPost("{collectionId:guid}/submit")]
    public async Task<ActionResult<SubmitMaturityAssessmentResultDto>> Submit(
        [FromRoute] Guid collectionId,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetOrganisationPermissions()
            .MatchAsync<ActionResult<SubmitMaturityAssessmentResultDto>>(
                async permission =>
                {
                    // Freeze the rendered assessment just before finalising. IsNone/ValueUnsafe, not Match —
                    // Option.Match throws on a null result (BLUM-7216).
                    var snapshot = await snapshotService.BuildAsync(permission, collectionId, cancellationToken);
                    var snapshotJson = snapshot.IsNone ? null : snapshotService.Serialize(snapshot.ValueUnsafe());

                    var result = await sender.Send(
                        new SubmitMaturityAssessmentCommand { CollectionId = collectionId, SnapshotJson = snapshotJson },
                        cancellationToken);

                    return result.Match<ActionResult<SubmitMaturityAssessmentResultDto>>(
                        assessment =>
                        {
                            telemetryClient.TrackEventIfEnabled(
                                "maturity_assessment_submitted",
                                new Dictionary<string, string>
                                {
                                    ["collectionId"] = collectionId.ToString(),
                                    ["referenceNumber"] = assessment.ReferenceNumber!
                                });
                            return new SubmitMaturityAssessmentResultDto(assessment.ReferenceNumber!, assessment.SubmittedAt!.Value);
                        },
                        error =>
                        {
                            telemetryClient.TrackEventIfEnabled(
                                "maturity_assessment_submit_failed",
                                new Dictionary<string, string>
                                {
                                    ["collectionId"] = collectionId.ToString(),
                                    ["reason"] = error.GetType().Name
                                });
                            return error.ToObjectResult();
                        });
                },
                error => StatusCode(StatusCodes.Status403Forbidden, error.Message));
    }

    // "Download your assessment" — 404 until submitted. The "export" segment puts it on the Export rate-limit budget.
    [HttpGet("{collectionId:guid}/export")]
    public async Task<ActionResult> Export(
        [FromRoute] Guid collectionId,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetOrganisationPermissions()
            .MatchAsync<ActionResult>(
                async permission =>
                {
                    var result = await exportService.GenerateAsync(permission, collectionId, cancellationToken);
                    return result.Match<ActionResult>(
                        file => File(file.Content, "application/zip", file.FileName),
                        () => NotFound());
                },
                error => StatusCode(StatusCodes.Status403Forbidden, error.Message));
    }

    // The read-only submitted copy for the LA history view — 404 until submitted.
    [HttpGet("{collectionId:guid}/submitted")]
    public async Task<ActionResult<SubmittedMaturityAssessmentDto>> Submitted(
        [FromRoute] Guid collectionId,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetOrganisationPermissions()
            .MatchAsync<ActionResult<SubmittedMaturityAssessmentDto>>(
                async permission =>
                {
                    var result = await controllerService.GetSubmittedAsync(permission, collectionId, cancellationToken);
                    return result.Match<ActionResult<SubmittedMaturityAssessmentDto>>(dto => dto, () => NotFound());
                },
                error => StatusCode(StatusCodes.Status403Forbidden, error.Message));
    }
}