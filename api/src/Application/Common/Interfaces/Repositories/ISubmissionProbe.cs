using Domain.Organisations;

namespace Application.Common.Interfaces.Repositories;

/// Lightweight probe for "has this organisation ever started a submission" checks (BLUM-6212 AC3). Implemented by
/// SubmissionRepository since BLUM-6611 introduced the Submissions aggregate.
public interface ISubmissionProbe
{
    Task<bool> HasAnyForOrganisationAsync(OrganisationId organisationId, CancellationToken cancellationToken = default);
}