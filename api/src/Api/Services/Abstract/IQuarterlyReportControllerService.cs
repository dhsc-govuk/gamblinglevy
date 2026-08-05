using Api.Dtos;
using Application.Common.Permissions;
using Domain.Submissions;
using LanguageExt;

namespace Api.Services.Abstract;

public interface IQuarterlyReportControllerService
{
    // One assigned quarterly collection plus the caller organisation's report state against it. None when the
    // collection does not exist, is not quarterly, or is not assigned to the caller's organisation.
    Task<Option<QuarterlyReportContextDto>> GetContextAsync(Permission permission, Guid collectionId, CancellationToken cancellationToken);

    Task<Option<SubmissionTaskListDto>> GetTaskListAsync(Permission permission, Guid collectionId, CancellationToken cancellationToken);

    // Folds a command-returned Submission into the task-list DTO (module names resolved from the forms builder).
    Task<SubmissionTaskListDto> ToTaskListDtoAsync(Submission submission, CancellationToken cancellationToken);

    Task<Option<IReadOnlyList<ReportableActivityDto>>> GetReportableActivitiesAsync(Permission permission, Guid collectionId, CancellationToken cancellationToken);

    Task<Option<ActivityReportQuestionsDto>> GetActivityQuestionsAsync(Permission permission, Guid collectionId, Guid activityId, CancellationToken cancellationToken);

    // The "Record funding spent" section state: reportable activities, the remaining forecast quarters, any saved
    // spend amounts, and the computed "Summary for all activities" figures.
    Task<Option<FundingSpendDto>> GetFundingSpendAsync(Permission permission, Guid collectionId, CancellationToken cancellationToken);

    // The "Provide strategic feedback" section state: the feedback questions grouped into their two pages plus any
    // saved answers and the rolled-up section status.
    Task<Option<StrategicFeedbackDto>> GetStrategicFeedbackAsync(Permission permission, Guid collectionId, CancellationToken cancellationToken);
}