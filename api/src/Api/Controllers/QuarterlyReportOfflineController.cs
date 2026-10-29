using Api.Common;
using Api.Dtos;
using Api.Modules.Errors;
using Api.Services.Abstract;
using Application.Common.Features;
using Application.Common.Telemetry;
using Application.Submissions;
using Application.Submissions.Commands.UploadOfflineReport;
using Application.Submissions.Exceptions;
using Domain.ValueObjects;
using MediatR;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

// "Complete your report offline" (BLUM-8310, behind BLUM_8310): a CSV template per section of the LA's quarterly
// report, and one upload that takes any of them back. Same audience as the rest of the quarterly-report surface.
//
// One route per section rather than a {section} parameter: the route table SubmissionAccessControlTests walks
// matches literal paths, and three fixed routes also keep the section out of anything a caller can make up.
[Route("quarterly-report")]
[ApiController]
[Authorize(Roles = Role.AnyOrganisationMember)]
[RequiresFeature(Feature.BLUM_8310)]
public class QuarterlyReportOfflineController(
    ISender sender,
    IQuarterlyReportOfflineControllerService offlineService,
    IQuarterlyReportControllerService quarterlyReportService,
    TelemetryClient telemetryClient)
    : ControllerBase
{
    // A little over the file cap, so an over-size file still reaches the handler and is refused with the page's own
    // message rather than Kestrel's bodiless 413.
    private const long RequestSizeLimit = 6 * 1024 * 1024;

    [HttpGet("{collectionId:guid}/offline/activity-results/template")]
    public Task<ActionResult> ActivityResultsTemplate([FromRoute] Guid collectionId, CancellationToken cancellationToken) =>
        TemplateAsync(collectionId, SubmissionModules.ActivityKeyResults, cancellationToken);

    [HttpGet("{collectionId:guid}/offline/funding-spent/template")]
    public Task<ActionResult> FundingSpentTemplate([FromRoute] Guid collectionId, CancellationToken cancellationToken) =>
        TemplateAsync(collectionId, SubmissionModules.FundingSpent, cancellationToken);

    [HttpGet("{collectionId:guid}/offline/strategic-feedback/template")]
    public Task<ActionResult> StrategicFeedbackTemplate([FromRoute] Guid collectionId, CancellationToken cancellationToken) =>
        TemplateAsync(collectionId, SubmissionModules.StrategicFeedback, cancellationToken);

    // A file parameter makes [ApiController] require a multipart body, so anything else is a bodiless 415 before the
    // action runs; a browser's form post always is multipart. Within that, the file is nullable so a form with no file
    // part still reaches the handler — and the report's gate — instead of being refused by model binding first.
    [HttpPost("{collectionId:guid}/offline/upload")]
    [RequestSizeLimit(RequestSizeLimit)]
    public async Task<ActionResult<OfflineUploadResultDto>> Upload(
        [FromRoute] Guid collectionId,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new UploadOfflineReportCommand { CollectionId = collectionId, File = file }, cancellationToken);
        return await result.MatchAsync<ActionResult<OfflineUploadResultDto>>(
            async upload =>
            {
                Track("offline_upload_succeeded", collectionId, upload.ModuleCode, errorCount: 0);
                return new OfflineUploadResultDto(upload.ModuleCode, await quarterlyReportService.ToTaskListDtoAsync(upload.Submission, cancellationToken));
            },
            error =>
            {
                if (error is OfflineUploadInvalidException invalid)
                {
                    Track("offline_upload_failed", collectionId, invalid.ModuleCode, invalid.Errors.Count);
                }

                return error.ToObjectResult();
            });
    }

    private async Task<ActionResult> TemplateAsync(Guid collectionId, string moduleCode, CancellationToken cancellationToken)
    {
        var result = await offlineService.GetTemplateAsync(collectionId, moduleCode, cancellationToken);
        return result.Match<ActionResult>(
            template => File(template.Content, "text/csv", template.FileName),
            error => error.ToObjectResult());
    }

    // Counts only: nothing from the file goes to telemetry.
    private void Track(string name, Guid collectionId, string moduleCode, int errorCount) =>
        telemetryClient.TrackEventIfEnabled(
            name,
            new Dictionary<string, string>
            {
                ["collectionId"] = collectionId.ToString(),
                ["section"] = moduleCode,
                ["errorCount"] = errorCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
}