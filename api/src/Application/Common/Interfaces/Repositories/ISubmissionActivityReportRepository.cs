using Domain.Activities;
using Domain.Submissions;
using LanguageExt;

namespace Application.Common.Interfaces.Repositories;

public interface ISubmissionActivityReportRepository
{
    Task<SubmissionActivityReport> AddAsync(SubmissionActivityReport report, CancellationToken cancellationToken = default);
    Task UpdateAsync(SubmissionActivityReport report, CancellationToken cancellationToken = default);
    Task<Option<SubmissionActivityReport>> GetForActivityAsync(SubmissionId submissionId, ActivityId activityId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubmissionActivityReport>> GetForSubmissionAsync(SubmissionId submissionId, CancellationToken cancellationToken = default);
}