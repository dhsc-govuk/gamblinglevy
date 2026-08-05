using Application.Common.Interfaces.Repositories;
using Domain.Activities;
using Domain.Submissions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class SubmissionActivityReportRepository(ApplicationDbContext dbContext)
    : RepositoryBase<SubmissionActivityReport, SubmissionActivityReportId>(dbContext), ISubmissionActivityReportRepository
{
    Task ISubmissionActivityReportRepository.UpdateAsync(SubmissionActivityReport report, CancellationToken cancellationToken)
        => UpdateAsync(report, cancellationToken);

    public async Task<Option<SubmissionActivityReport>> GetForActivityAsync(
        SubmissionId submissionId,
        ActivityId activityId,
        CancellationToken cancellationToken = default)
    {
        var entity = await Context.SubmissionActivityReports
            .FirstOrDefaultAsync(
                r => r.SubmissionId == submissionId && r.ActivityId == activityId,
                cancellationToken);

        return entity is null ? Option<SubmissionActivityReport>.None : Option<SubmissionActivityReport>.Some(entity);
    }

    public async Task<IReadOnlyList<SubmissionActivityReport>> GetForSubmissionAsync(
        SubmissionId submissionId,
        CancellationToken cancellationToken = default)
    {
        return await Context.SubmissionActivityReports
            .AsNoTracking()
            .Where(r => r.SubmissionId == submissionId)
            .ToListAsync(cancellationToken);
    }
}