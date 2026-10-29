using Application.Common.Interfaces.Queries;
using Domain.MaturityAssessments;
using Domain.Organisations;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

// Read side only: the rows are written by MaturityBaselineSeeder through the DbContext directly, like every other
// boot seeder.
public class MaturityBaselineAnswerRepository(ApplicationDbContext dbContext)
    : RepositoryBase<MaturityBaselineAnswer, MaturityBaselineAnswerId>(dbContext), IMaturityBaselineQueries
{
    public async Task<IReadOnlyList<MaturityBaselineAnswer>> GetForOrganisationAsync(
        OrganisationId organisationId,
        int surveyYear,
        CancellationToken cancellationToken = default)
    {
        return await Context.MaturityBaselineAnswers
            .AsNoTracking()
            .Where(a => a.OrganisationId == organisationId && a.SurveyYear == surveyYear)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> GetOrganisationIdsWithSurveyYearAsync(
        int surveyYear,
        CancellationToken cancellationToken = default)
    {
        var ids = await Context.MaturityBaselineAnswers
            .AsNoTracking()
            .Where(a => a.SurveyYear == surveyYear)
            .Select(a => a.OrganisationId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return ids.Select(id => id.Value).ToHashSet();
    }
}