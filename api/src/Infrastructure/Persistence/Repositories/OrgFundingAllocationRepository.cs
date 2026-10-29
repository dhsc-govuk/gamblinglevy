using Application.Common.Interfaces.Repositories;
using Domain.Funding;
using Domain.Organisations;
using LanguageExt;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class OrgFundingAllocationRepository(ApplicationDbContext dbContext)
    : RepositoryBase<OrgFundingAllocation, OrgFundingAllocationId>(dbContext), IOrgFundingAllocationRepository
{
    public async Task<Option<OrgFundingAllocation>> FindForOrganisationAndWindowAsync(
        OrganisationId organisationId,
        FundingWindowId fundingWindowId,
        CancellationToken cancellationToken = default)
    {
        var entity = await Context.OrgFundingAllocations
            .FirstOrDefaultAsync(x => x.OrganisationId == organisationId && x.FundingWindowId == fundingWindowId, cancellationToken);
        return entity ?? Option<OrgFundingAllocation>.None;
    }

    public async Task<IReadOnlyList<OrgAllocationWithWindow>> ListWithWindowsForOrganisationsAsync(
        IReadOnlyCollection<OrganisationId> organisationIds,
        CancellationToken cancellationToken = default)
    {
        if (organisationIds.Count == 0)
        {
            return [];
        }

        // Materialised as the strongly-typed ids rather than their Guid values: OrganisationId maps through
        // HasConversion, so Contains over the id records translates to the same IN (...) on organisation_id.
        var ids = organisationIds.Distinct().ToList();

        return await Context.OrgFundingAllocations
            .AsNoTracking()
            .Where(allocation => ids.Contains(allocation.OrganisationId))
            .Join(
                Context.FundingWindows.AsNoTracking(),
                allocation => allocation.FundingWindowId,
                window => window.Id,
                (allocation, window) => new OrgAllocationWithWindow(allocation, window))
            .ToListAsync(cancellationToken);
    }

    public async Task RemoveByOrganisationAsync(OrganisationId organisationId, CancellationToken cancellationToken = default)
    {
        var rows = await Context.OrgFundingAllocations
            .Where(x => x.OrganisationId == organisationId)
            .ToListAsync(cancellationToken);

        if (rows.Count > 0)
        {
            Context.OrgFundingAllocations.RemoveRange(rows);
            await Context.SaveChangesAsync(cancellationToken);
        }
    }
}