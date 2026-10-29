using Domain.MaturityAssessments;
using Domain.Organisations;

namespace Application.Common.Interfaces.Queries;

public interface IMaturityBaselineQueries
{
    // The organisation's imported prior-year survey answers for one survey year. AsNoTracking.
    Task<IReadOnlyList<MaturityBaselineAnswer>> GetForOrganisationAsync(OrganisationId organisationId, int surveyYear, CancellationToken cancellationToken = default);

    // Every organisation that already holds rows for the survey year — the seeder's per-organisation guard.
    Task<IReadOnlySet<Guid>> GetOrganisationIdsWithSurveyYearAsync(int surveyYear, CancellationToken cancellationToken = default);
}