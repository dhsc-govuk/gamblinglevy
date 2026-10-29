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
    /// <summary>
    /// Every allocation held by each of the given organisations, paired with the funding window that names its
    /// financial year, in ONE query.
    ///
    /// Exists because an organisation now holds more than one allocation (BLUM-7608) and every caller has to know
    /// which financial year each amount belongs to. It replaced FindLatestForOrganisationAsync, which is
    /// deliberately GONE rather than merely unused: that method ordered by CreatedAt, which was unambiguous while
    /// an organisation had one row and became "the row written last" - i.e. the UPCOMING year - the moment create
    /// and amend could write two in a single request. Every caller that kept it would have amended, audited or
    /// reported against next year's money. Pair this with AllocationSelector to choose a year.
    ///
    /// It also removes an N+1: OrganisationsControllerService called the old method once per organisation inside
    /// its list loop, which would have become three calls once the window and the upcoming year were needed too.
    /// </summary>
    Task<IReadOnlyList<OrgAllocationWithWindow>> ListWithWindowsForOrganisationsAsync(
        IReadOnlyCollection<OrganisationId> organisationIds,
        CancellationToken cancellationToken = default);
    Task<OrgFundingAllocation> AddAsync(OrgFundingAllocation entity, CancellationToken cancellationToken = default);
    Task<OrgFundingAllocation> UpdateAsync(OrgFundingAllocation entity, CancellationToken cancellationToken = default);
    Task RemoveByOrganisationAsync(OrganisationId organisationId, CancellationToken cancellationToken = default);
}

/// <summary>
/// One organisation's allocation together with the window naming its financial year. A record rather than a
/// tuple so the two halves stay named as this crosses the Application boundary into DTO assembly.
/// </summary>
public record OrgAllocationWithWindow(OrgFundingAllocation Allocation, FundingWindow Window);