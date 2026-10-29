using Api.Common;
using Api.Dtos;
using Api.Modules.Errors;
using Api.Services.Abstract;
using Application.Activities.Commands;
using Application.Activities.Commands.CreateActivity;
using Application.Activities.Commands.RestoreActivity;
using Application.Activities.Commands.SoftDeleteActivity;
using Application.Activities.Commands.UpdateActivity;
using Application.Activities.Exceptions;
using Application.Common.Permissions;
using Domain.Activities;
using Domain.ValueObjects;
using LanguageExt;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[Route("activities")]
[ApiController]
[Authorize(Roles = Role.AnyOrganisationMember)]
public class ActivitiesController(
    PermissionsService permissionsService,
    ISender sender,
    IActivitiesControllerService controllerService,
    IFormDesignerControllerService formDesignerControllerService,
    IActivityRecordExportService activityRecordExportService)
    : ControllerBase
{
    // The active activity-record form schema (DHSC-authored dynamic questions) rendered in the create/edit flow.
    // Pass categoryId to scope the schema to the chosen activity category (plus any global sections).
    [HttpGet("form-schema")]
    public async Task<ActionResult<FormSchemaDto>> FormSchema(
        [FromQuery] Guid? categoryId,
        CancellationToken cancellationToken)
    {
        return categoryId.HasValue
            ? await formDesignerControllerService.GetActiveSchemaForCategoryAsync(categoryId, cancellationToken)
            : await formDesignerControllerService.GetActiveSchemaAsync(cancellationToken);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ActivitySummaryDto>>> List(
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        // ActivityRepository returns an empty list for an unrecognised status, which is indistinguishable
        // from "this organisation has no activities" — a silently wrong answer rather than an error
        // (BLUM-7240 AC4). Normalised the same way the repository normalises it, so a filter that worked
        // before still works; "draft" is allowed because it is a pseudo-status mapped onto IsComplete.
        QueryGuard.AllowedValue(
            status?.Trim().ToLowerInvariant(),
            value => ActivityStatus.IsSupported(value) || value == ActivityStatuses.DraftFilter,
            nameof(status),
            "Select a valid status.");

        return await permissionsService.GetOrganisationPermissions()
            .MatchAsync<ActionResult<IReadOnlyList<ActivitySummaryDto>>>(
                async permission => Ok(await controllerService.ListAsync(permission, status, cancellationToken)),
                error => StatusCode(StatusCodes.Status403Forbidden, error.Message));
    }

    [HttpGet("review")]
    public async Task<ActionResult<IReadOnlyList<ActivitySummaryDto>>> Review(CancellationToken cancellationToken)
    {
        return await permissionsService.GetOrganisationPermissions()
            .MatchAsync<ActionResult<IReadOnlyList<ActivitySummaryDto>>>(
                async permission => Ok(await controllerService.ReviewAsync(permission, cancellationToken)),
                error => StatusCode(StatusCodes.Status403Forbidden, error.Message));
    }

    [HttpGet("deleted")]
    public async Task<ActionResult<IReadOnlyList<ActivitySummaryDto>>> Deleted(CancellationToken cancellationToken)
    {
        return await permissionsService.GetOrganisationPermissions()
            .MatchAsync<ActionResult<IReadOnlyList<ActivitySummaryDto>>>(
                async permission => Ok(await controllerService.DeletedAsync(permission, cancellationToken)),
                error => StatusCode(StatusCodes.Status403Forbidden, error.Message));
    }

    [HttpGet("taxonomy")]
    public async Task<ActionResult<ActivityTaxonomyDto>> Taxonomy(CancellationToken cancellationToken)
    {
        return await controllerService.GetTaxonomyAsync(cancellationToken);
    }

    [HttpGet("{activityId:guid}")]
    public async Task<ActionResult<ActivityDto>> Get(
        [FromRoute] Guid activityId,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetOrganisationPermissions()
            .MatchAsync<ActionResult<ActivityDto>>(
                async permission =>
                {
                    var result = await controllerService.GetAsync(permission, activityId, cancellationToken);
                    return result.Match<ActionResult<ActivityDto>>(dto => dto, () => NotFound());
                },
                error => StatusCode(StatusCodes.Status403Forbidden, error.Message));
    }

    // Downloads the whole activity record as a CSV — every field entered on the activity, plus what the organisation's
    // submitted quarterly reports recorded for it (BLUM-7648). Both LA roles, like the page it is downloaded from;
    // another organisation's or a deleted activity is a 404, as on GET. The "export" route segment is what puts it on
    // the Export rate-limit budget (RateLimitPolicyResolver).
    [HttpGet("{activityId:guid}/export")]
    public async Task<ActionResult> Export(
        [FromRoute] Guid activityId,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetOrganisationPermissions()
            .MatchAsync<ActionResult>(
                async permission =>
                {
                    var result = await activityRecordExportService.GenerateAsync(permission, activityId, cancellationToken);
                    return result.Match<ActionResult>(
                        file => File(file.Content, "text/csv", file.FileName),
                        () => NotFound());
                },
                error => StatusCode(StatusCodes.Status403Forbidden, error.Message));
    }

    [HttpPost]
    [AcceptsEncodedJsonBody]
    public async Task<ActionResult<ActivityDto>> Create(
        [FromBody] CreateActivityRequestDto request,
        CancellationToken cancellationToken)
    {
        var command = new CreateActivityCommand
        {
            Title = request.Title,
            CategoryId = request.CategoryId,
            TaskId = request.TaskId,
            CustomTaskText = request.CustomTaskText,
            Status = request.Status,
            StartMonth = request.StartMonth,
            StartYear = request.StartYear,
            ClosureReason = request.ClosureReason,
            ExpectedResults = MapExpectedResults(request.ExpectedResults),
            Answers = MapAnswers(request.Answers),
            SaveAsDraft = request.SaveAsDraft,
        };

        var result = await sender.Send(command, cancellationToken);
        return await ToActionResult(result, cancellationToken);
    }

    [HttpPut("{activityId:guid}")]
    [AcceptsEncodedJsonBody]
    public async Task<ActionResult<ActivityDto>> Update(
        [FromRoute] Guid activityId,
        [FromBody] UpdateActivityRequestDto request,
        CancellationToken cancellationToken)
    {
        var command = new UpdateActivityCommand
        {
            Id = activityId,
            Title = request.Title,
            CategoryId = request.CategoryId,
            TaskId = request.TaskId,
            CustomTaskText = request.CustomTaskText,
            Status = request.Status,
            StartMonth = request.StartMonth,
            StartYear = request.StartYear,
            ClosureReason = request.ClosureReason,
            ExpectedResults = MapExpectedResults(request.ExpectedResults),
            Answers = MapAnswers(request.Answers),
            SaveAsDraft = request.SaveAsDraft,
        };

        var result = await sender.Send(command, cancellationToken);
        return await ToActionResult(result, cancellationToken);
    }

    [HttpPost("{activityId:guid}/restore")]
    public async Task<ActionResult<ActivityDto>> Restore(
        [FromRoute] Guid activityId,
        CancellationToken cancellationToken)
    {
        var command = new RestoreActivityCommand { Id = activityId };
        var result = await sender.Send(command, cancellationToken);
        return await ToActionResult(result, cancellationToken);
    }

    [HttpDelete("{activityId:guid}")]
    public async Task<ActionResult<ActivityDto>> Delete(
        [FromRoute] Guid activityId,
        CancellationToken cancellationToken)
    {
        var command = new SoftDeleteActivityCommand { Id = activityId };
        var result = await sender.Send(command, cancellationToken);
        return await ToActionResult(result, cancellationToken);
    }

    private async Task<ActionResult<ActivityDto>> ToActionResult(
        Either<ActivityException, Activity> result,
        CancellationToken cancellationToken)
    {
        return await result.MatchAsync<ActionResult<ActivityDto>>(
            async activity => await controllerService.ToDtoAsync(activity, cancellationToken),
            error => error.ToObjectResult());
    }

    private static IReadOnlyList<ActivityAnswerInput> MapAnswers(IReadOnlyList<ActivityAnswerInputDto>? answers) =>
        answers?.Select(a => new ActivityAnswerInput(a.QuestionCode, a.Value)).ToList() ?? [];

    private static IReadOnlyList<ExpectedResultInput> MapExpectedResults(IReadOnlyList<ExpectedResultInputDto>? expectedResults) =>
        expectedResults?.Select(r => new ExpectedResultInput(r.KeyResultCode, r.Text)).ToList() ?? [];
}