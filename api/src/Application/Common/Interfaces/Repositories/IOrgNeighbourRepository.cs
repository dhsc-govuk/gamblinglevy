using Domain.Organisations;

namespace Application.Common.Interfaces.Repositories;

public interface IOrgNeighbourRepository
{
    Task<IReadOnlyList<OrgNeighbour>> GetForOrganisationAsync(OrganisationId organisationId, CancellationToken cancellationToken = default);
    Task RemoveByOrganisationAsync(OrganisationId organisationId, CancellationToken cancellationToken = default);
}