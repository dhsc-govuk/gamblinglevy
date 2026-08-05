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

    public async Task<Option<OrgFundingAllocation>> FindLatestForOrganisationAsync(
        OrganisationId organisationId,
        CancellationToken cancellationToken = default)
    {
        var entity = await Context.OrgFundingAllocations
            .AsNoTracking()
            .Where(x => x.OrganisationId == organisationId)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return entity ?? Option<OrgFundingAllocation>.None;
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