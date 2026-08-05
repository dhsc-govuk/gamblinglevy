using Domain.Funding;
using Domain.Organisations;
using LanguageExt;

namespace Application.Common.Interfaces.Repositories;

public interface IOrgFundingAllocationRepository
{
    Task<Option<OrgFundingAllocation>> FindForOrganisationAndWindowAsync(
        OrganisationId organisationId,
        FundingWindowId fundingWindowId,
        CancellationToken cancellationToken = default);
    Task<Option<OrgFundingAllocation>> FindLatestForOrganisationAsync(
        OrganisationId organisationId,
        CancellationToken cancellationToken = default);
    Task<OrgFundingAllocation> AddAsync(OrgFundingAllocation entity, CancellationToken cancellationToken = default);
    Task<OrgFundingAllocation> UpdateAsync(OrgFundingAllocation entity, CancellationToken cancellationToken = default);
    Task RemoveByOrganisationAsync(OrganisationId organisationId, CancellationToken cancellationToken = default);
}