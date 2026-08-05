using Application.Common.Interfaces.Repositories;
using Domain.Organisations;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class OrgNeighbourRepository(ApplicationDbContext dbContext)
    : RepositoryBase<OrgNeighbour, OrgNeighbourId>(dbContext), IOrgNeighbourRepository
{
    public async Task<IReadOnlyList<OrgNeighbour>> GetForOrganisationAsync(OrganisationId organisationId, CancellationToken cancellationToken = default)
    {
        return await Context.OrgNeighbours
            .AsNoTracking()
            .Where(x => x.OrganisationId == organisationId)
            .ToListAsync(cancellationToken);
    }

    public async Task RemoveByOrganisationAsync(OrganisationId organisationId, CancellationToken cancellationToken = default)
    {
        var rows = await Context.OrgNeighbours
            .Where(x => x.OrganisationId == organisationId || x.NeighbourOrgId == organisationId)
            .ToListAsync(cancellationToken);

        if (rows.Count > 0)
        {
            Context.OrgNeighbours.RemoveRange(rows);
            await Context.SaveChangesAsync(cancellationToken);
        }
    }
}