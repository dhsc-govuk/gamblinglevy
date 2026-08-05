using Api.Dtos;
using Api.Modules.Errors;
using Api.Services.Abstract;
using Application.Activities.Commands;
using Application.Common.Permissions;
using Application.Common.Telemetry;
using Application.Submissions.Commands.CompleteActivityReport;
using Application.Submissions.Commands.CompleteFundingSpent;
using Application.Submissions.Commands.CompleteStrategicFeedback;
using Application.Submissions.Commands.ConfirmActivities;
using Application.Submissions.Commands.CreateSubmission;
using Application.Submissions.Commands.SaveActivityReport;
using Application.Submissions.Commands.SaveFundingActuals;
using Application.Submissions.Commands.SaveFundingForecast;
using Application.Submissions.Commands.SaveStrategicFeedback;
using Application.Submissions.Commands.SubmitReport;
using Domain.ValueObjects;
using LanguageExt.UnsafeValueAccess;
using MediatR;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

// LA-facing quarterly-report surface (BLUM-6611): the "Submit your quarterly report" intro context, the task list,
// the "confirm your activities" step and the "Enter activity key results" section. Every LA role may both read and
// submit — the view-only split that once separated finance/read-only users from activity managers went away with
// those roles (BLUM-7038).
[Route("quarterly-report")]
[ApiController]
[Authorize(Roles = Role.AnyOrganisationMember)]
public class QuarterlyReportController(
    PermissionsService permissionsService,
    ISender sender,
    IQuarterlyReportControllerService controllerService,
    ILaSubmissionsControllerService laSubmissionsService,
    IReportSnapshotService snapshotService,
    IQuarterlyReportExportService exportService,
    TelemetryClient telemetryClient)
    : ControllerBase
{
    // The "Submit your quarterly report" context for one collection. Collection-scoped rather than resolved from
    // "the organisation's current open window": two quarterly windows can be open at once, so an implicit pick
    // opened (and would have authored) the wrong quarter — BLUM-6950. 404 when the collection does not exist, is a
    // draft, is not quarterly, or is not assigned to the caller's organisation (anti-probing).
    [HttpGet("{collectionId:guid}/context")]
    public async Task<ActionResult<QuarterlyReportContextDto>> Context(
        [FromRoute] Guid collectionId,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetOrganisationPermissions()
            .MatchAsync<ActionResult<QuarterlyReportContextDto>>(
                async permission =>
                {
                    var result = await controllerService.GetContextAsync(permission, collectionId, cancellationToken);
                    return result.Match<ActionResult<QuarterlyReportContextDto>>(dto => dto, () => NotFound());
                },
                error => StatusCode(StatusCodes.Status403Forbidden, error.Message));
    }

    // The caller's own quarterly collections with derived collection + submission status (BLUM-6097). Feeds the LA
    // "Reports" landing (Open data collections) and the "Previously submitted reports" list. Org-scoped to the caller.
    [HttpGet("collections")]
    public async Task<ActionResult<IReadOnlyList<MyCollectionSummaryDto>>> Collections(CancellationToken cancellationToken)
    {
        return await permissionsService.GetOrganisationPermissions()
            .MatchAsync<ActionResult<IReadOnlyList<MyCollectionSummaryDto>>>(
                async permission => Ok(await laSubmissionsService.GetMyCollectionsAsync(permission, cancellationToken)),
                error => StatusCode(StatusCodes.Status403Forbidden, error.Message));
    }

    // The caller's own submission detail for one collection (BLUM-6098). 404 when the collection does not exist, is
    // not quarterly, or the caller's org is neither assigned to it nor has a submission for it (anti-probing).
    [HttpGet("submissions/{collectionId:guid}")]
    public async Task<ActionResult<MySubmissionDetailDto>> SubmissionDetail(
        [FromRoute] Guid collectionId,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetOrganisationPermissions()
            .MatchAsync<ActionResult<MySubmissionDetailDto>>(
                async permission =>
                {
                    var result = await laSubmissionsService.GetMySubmissionDetailAsync(permission, collectionId, cancellationToken);
                    return result.Match<ActionResult<MySubmissionDetailDto>>(dto => dto, () => NotFound());
                },
                error => StatusCode(StatusCodes.Status403Forbidden, error.Message));
    }

    [HttpPost("{collectionId:guid}/submission")]
    public async Task<ActionResult<SubmissionTaskListDto>> CreateSubmission(
        [FromRoute] Guid collectionId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CreateSubmissionCommand { CollectionId = collectionId }, cancellationToken);
        return await result.MatchAsync<ActionResult<SubmissionTaskListDto>>(
            async submission =>
            {
                telemetryClient.TrackEventIfEnabled(
                    "report_started",
                    new Dictionary<string, string> { ["collectionId"] = collectionId.ToString() });
                return await controllerService.ToTaskListDtoAsync(submission, cancellationToken);
            },
            error => error.ToObjectResult());
    }

    [HttpGet("{collectionId:guid}/task-list")]
    public async Task<ActionResult<SubmissionTaskListDto>> TaskList(
        [FromRoute] Guid collectionId,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetOrganisationPermissions()
            .MatchAsync<ActionResult<SubmissionTaskListDto>>(
                async permission =>
                {
                    var result = await controllerService.GetTaskListAsync(permission, collectionId, cancellationToken);
                    return result.Match<ActionResult<SubmissionTaskListDto>>(dto => dto, () => NotFound());
                },
                error => StatusCode(StatusCodes.Status403Forbidden, error.Message));
    }

    [HttpPost("{collectionId:guid}/confirm-activities")]
    public async Task<ActionResult<SubmissionTaskListDto>> ConfirmActivities(
        [FromRoute] Guid collectionId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ConfirmActivitiesCommand { CollectionId = collectionId }, cancellationToken);
        return await result.MatchAsync<ActionResult<SubmissionTaskListDto>>(
            async submission => await controllerService.ToTaskListDtoAsync(submission, cancellationToken),
            error => error.ToObjectResult());
    }

    [HttpGet("{collectionId:guid}/activities")]
    public async Task<ActionResult<IReadOnlyList<ReportableActivityDto>>> ReportableActivities(
        [FromRoute] Guid collectionId,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetOrganisationPermissions()
            .MatchAsync<ActionResult<IReadOnlyList<ReportableActivityDto>>>(
                async permission =>
                {
                    var result = await controllerService.GetReportableActivitiesAsync(permission, collectionId, cancellationToken);
                    return result.Match<ActionResult<IReadOnlyList<ReportableActivityDto>>>(rows => Ok(rows), () => NotFound());
                },
                error => StatusCode(StatusCodes.Status403Forbidden, error.Message));
    }

    [HttpGet("{collectionId:guid}/activities/{activityId:guid}/questions")]
    public async Task<ActionResult<ActivityReportQuestionsDto>> ActivityQuestions(
        [FromRoute] Guid collectionId,
        [FromRoute] Guid activityId,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetOrganisationPermissions()
            .MatchAsync<ActionResult<ActivityReportQuestionsDto>>(
                async permission =>
                {
                    var result = await controllerService.GetActivityQuestionsAsync(permission, collectionId, activityId, cancellationToken);
                    return result.Match<ActionResult<ActivityReportQuestionsDto>>(dto => dto, () => NotFound());
                },
                error => StatusCode(StatusCodes.Status403Forbidden, error.Message));
    }

    // Saves one page of an activity's answers — only the submitted question codes are replaced (per-page merge).
    // SaveAsDraft = "Save and come back later". Never completes the report; that is the complete endpoint below.
    [HttpPut("{collectionId:guid}/activities/{activityId:guid}/answers")]
    public async Task<ActionResult<ActivityReportStateDto>> SaveAnswers(
        [FromRoute] Guid collectionId,
        [FromRoute] Guid activityId,
        [FromBody] SaveActivityReportRequestDto request,
        CancellationToken cancellationToken)
    {
        var command = new SaveActivityReportCommand
        {
            CollectionId = collectionId,
            ActivityId = activityId,
            Answers = request.Answers?.Select(a => new ActivityAnswerInput(a.QuestionCode, a.Value)).ToList() ?? [],
            SaveAsDraft = request.SaveAsDraft,
        };

        var result = await sender.Send(command, cancellationToken);
        return result.Match<ActionResult<ActivityReportStateDto>>(
            report => new ActivityReportStateDto(report.ActivityId.Value, report.Status),
            error => error.ToObjectResult());
    }

    // The check-your-answers Continue for one activity: no body — the saved draft answers are validated and the
    // activity's report marked Completed.
    [HttpPost("{collectionId:guid}/activities/{activityId:guid}/complete")]
    public async Task<ActionResult<ActivityReportStateDto>> CompleteActivityReport(
        [FromRoute] Guid collectionId,
        [FromRoute] Guid activityId,
        CancellationToken cancellationToken)
    {
        var command = new CompleteActivityReportCommand { CollectionId = collectionId, ActivityId = activityId };
        var result = await sender.Send(command, cancellationToken);
        return result.Match<ActionResult<ActivityReportStateDto>>(
            report => new ActivityReportStateDto(report.ActivityId.Value, report.Status),
            error => error.ToObjectResult());
    }

    [HttpGet("{collectionId:guid}/funding-spend")]
    public async Task<ActionResult<FundingSpendDto>> FundingSpend(
        [FromRoute] Guid collectionId,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetOrganisationPermissions()
            .MatchAsync<ActionResult<FundingSpendDto>>(
                async permission =>
                {
                    var result = await controllerService.GetFundingSpendAsync(permission, collectionId, cancellationToken);
                    return result.Match<ActionResult<FundingSpendDto>>(dto => dto, () => NotFound());
                },
                error => StatusCode(StatusCodes.Status403Forbidden, error.Message));
    }

    // Saves the "Activity spending" screen (actual levy spent this quarter). SaveAsDraft = "Save and come back later".
    [HttpPut("{collectionId:guid}/funding-spend/actuals")]
    public async Task<ActionResult<FundingSpendStateDto>> SaveFundingActuals(
        [FromRoute] Guid collectionId,
        [FromBody] SaveFundingActualsRequestDto request,
        CancellationToken cancellationToken)
    {
        var command = new SaveFundingActualsCommand
        {
            CollectionId = collectionId,
            OtherAmount = request.OtherAmount,
            Activities = request.Activities?.Select(a => new ActivitySpendInput(a.ActivityId, a.Amount)).ToList() ?? [],
            SaveAsDraft = request.SaveAsDraft,
        };

        var result = await sender.Send(command, cancellationToken);
        return result.Match<ActionResult<FundingSpendStateDto>>(
            status => new FundingSpendStateDto(status.ToString()),
            error => error.ToObjectResult());
    }

    // Saves the "Forecast spend" screen (expected levy spend per remaining quarter).
    [HttpPut("{collectionId:guid}/funding-spend/forecast")]
    public async Task<ActionResult<FundingSpendStateDto>> SaveFundingForecast(
        [FromRoute] Guid collectionId,
        [FromBody] SaveFundingForecastRequestDto request,
        CancellationToken cancellationToken)
    {
        var command = new SaveFundingForecastCommand
        {
            CollectionId = collectionId,
            Activities = request.Activities?
                .Select(a => new ActivityForecastInput(
                    a.ActivityId,
                    a.Quarters?.Select(q => new QuarterSpendInput(q.Quarter, q.Amount)).ToList() ?? []))
                .ToList() ?? [],
            Other = request.Other?.Select(q => new QuarterSpendInput(q.Quarter, q.Amount)).ToList() ?? [],
            SaveAsDraft = request.SaveAsDraft,
        };

        var result = await sender.Send(command, cancellationToken);
        return result.Match<ActionResult<FundingSpendStateDto>>(
            status => new FundingSpendStateDto(status.ToString()),
            error => error.ToObjectResult());
    }

    // Completes the "Record funding spent" section (the "Check your spending totals" Continue).
    [HttpPost("{collectionId:guid}/funding-spend/complete")]
    public async Task<ActionResult<FundingSpendStateDto>> CompleteFundingSpend(
        [FromRoute] Guid collectionId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CompleteFundingSpentCommand { CollectionId = collectionId }, cancellationToken);
        return result.Match<ActionResult<FundingSpendStateDto>>(
            status => new FundingSpendStateDto(status.ToString()),
            error => error.ToObjectResult());
    }

    [HttpGet("{collectionId:guid}/strategic-feedback")]
    public async Task<ActionResult<StrategicFeedbackDto>> StrategicFeedback(
        [FromRoute] Guid collectionId,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetOrganisationPermissions()
            .MatchAsync<ActionResult<StrategicFeedbackDto>>(
                async permission =>
                {
                    var result = await controllerService.GetStrategicFeedbackAsync(permission, collectionId, cancellationToken);
                    return result.Match<ActionResult<StrategicFeedbackDto>>(dto => dto, () => NotFound());
                },
                error => StatusCode(StatusCodes.Status403Forbidden, error.Message));
    }

    // Saves the "Provide strategic feedback" answers (the full set on every save). SaveAsDraft = "Save and come back
    // later"; a non-draft save validates every required answer.
    [HttpPut("{collectionId:guid}/strategic-feedback")]
    public async Task<ActionResult<StrategicFeedbackStateDto>> SaveStrategicFeedback(
        [FromRoute] Guid collectionId,
        [FromBody] SaveStrategicFeedbackRequestDto request,
        CancellationToken cancellationToken)
    {
        var command = new SaveStrategicFeedbackCommand
        {
            CollectionId = collectionId,
            Answers = request.Answers?.Select(a => new FeedbackAnswerInput(a.QuestionCode, a.Value)).ToList() ?? [],
            SaveAsDraft = request.SaveAsDraft,
        };

        var result = await sender.Send(command, cancellationToken);
        return result.Match<ActionResult<StrategicFeedbackStateDto>>(
            status => new StrategicFeedbackStateDto(status.ToString()),
            error => error.ToObjectResult());
    }

    // Completes the "Provide strategic feedback" section (the "Check your answers" Continue).
    [HttpPost("{collectionId:guid}/strategic-feedback/complete")]
    public async Task<ActionResult<StrategicFeedbackStateDto>> CompleteStrategicFeedback(
        [FromRoute] Guid collectionId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CompleteStrategicFeedbackCommand { CollectionId = collectionId }, cancellationToken);
        return result.Match<ActionResult<StrategicFeedbackStateDto>>(
            status => new StrategicFeedbackStateDto(status.ToString()),
            error => error.ToObjectResult());
    }

    // "Submit your quarterly report" — finalises the report (irreversible). 422 REPORT_INCOMPLETE when a section is
    // still incomplete, ACTIVITIES_NOT_CONFIRMED when the list is unconfirmed, 409 when already submitted.
    [HttpPost("{collectionId:guid}/submission/submit")]
    public async Task<ActionResult<SubmitReportResultDto>> SubmitReport(
        [FromRoute] Guid collectionId,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetOrganisationPermissions()
            .MatchAsync<ActionResult<SubmitReportResultDto>>(
                async permission =>
                {
                    // Freeze the rendered report from the live data just before finalising, so the downloadable "copy
                    // for your records" never drifts when activities/forms are edited afterwards. The command persists
                    // it atomically with the submit stamp; a null snapshot falls back to a live render at export time.
                    var snapshot = await snapshotService.BuildAsync(permission, collectionId, cancellationToken);

                    // IsNone/ValueUnsafe, not Match — Option.Match throws on a null result (BLUM-7216), which would
                    // turn "nothing to snapshot" into a 500 instead of the intended null-snapshot fallback.
                    var snapshotJson = snapshot.IsNone ? null : snapshotService.Serialize(snapshot.ValueUnsafe());

                    var result = await sender.Send(
                        new SubmitReportCommand { CollectionId = collectionId, ReportSnapshotJson = snapshotJson },
                        cancellationToken);

                    return result.Match<ActionResult<SubmitReportResultDto>>(
                        submission =>
                        {
                            telemetryClient.TrackEventIfEnabled(
                                "report_submitted",
                                new Dictionary<string, string>
                                {
                                    ["collectionId"] = collectionId.ToString(),
                                    ["referenceNumber"] = submission.ReferenceNumber!
                                });
                            return new SubmitReportResultDto(submission.ReferenceNumber!, submission.SubmittedAt!.Value);
                        },
                        error =>
                        {
                            telemetryClient.TrackEventIfEnabled(
                                "report_submit_failed",
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

    // Downloads the submitted report as a ZIP of CSVs ("Download your report"). All organisation members can
    // download; 404 until the report is submitted (the export is only the immutable, finalised copy).
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
}