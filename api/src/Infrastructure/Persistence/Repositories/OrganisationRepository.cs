using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Domain.Organisations;
using Domain.ValueObjects;
using LanguageExt;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;
public class OrganisationRepository(ApplicationDbContext dbContext)
    : RepositoryBase<Organisation, OrganisationId>(dbContext), IOrganisationRepository, IOrganisationQueries
{
    public async Task<int> Count(CancellationToken cancellationToken = default)
    {
        return await Context.Organisations.CountAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Organisation>> GetOrganisations(CancellationToken cancellationToken = default)
    {
        return await Context.Organisations.AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Organisation>> GetLocalAuthorities(string? onboardingStatus, CancellationToken cancellationToken = default)
    {
        var query = Context.Organisations.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(onboardingStatus))
        {
            query = query.Where(x => x.OnboardingStatus == OnboardingStatus.From(onboardingStatus));
        }

        return await query.OrderBy(x => x.Name).ToListAsync(cancellationToken);
    }

    public async Task<Option<Organisation>> GetOrganisationById(OrganisationId id, CancellationToken cancellationToken = default)
    {
        var entity = await Context.Organisations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return entity ?? Option<Organisation>.None;
    }

    public async Task<Option<Organisation>> GetOrganisationById(OrganisationId id, Permission permission, CancellationToken cancellationToken = default)
    {
        var entity = await GetQuery(permission.UserRole, permission.OrganisationId).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return entity ?? Option<Organisation>.None;
    }

    public async Task<Option<Organisation>> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var entity = await Context.Organisations.AsNoTracking().FirstOrDefaultAsync(x => x.Name == name, cancellationToken);
        return entity ?? Option<Organisation>.None;
    }

    // Case-insensitive: the ONS code is the bulk import's only match key and is free text on the edit form,
    // so an organisation stored as "e06000001" must still match the canonical "E06000001" in an uploaded
    // extract. An ordinal comparison silently turned that into a rejected create instead of an update.
    public async Task<Option<Organisation>> FindByOnsCodeAsync(string onsCode, CancellationToken cancellationToken = default)
    {
        var normalised = onsCode.ToUpper();
        var entity = await Context.Organisations.FirstOrDefaultAsync(x => x.OnsCode != null && x.OnsCode.ToUpper() == normalised, cancellationToken);
        return entity ?? Option<Organisation>.None;
    }

    public async Task<Option<Organisation>> FindDuplicateAsync(string targetName, OrganisationId sourceId, CancellationToken cancellationToken = default)
    {
        var entity = await Context.Organisations.AsNoTracking().FirstOrDefaultAsync(x => x.Name == targetName && x.Id != sourceId, cancellationToken);
        return entity ?? Option<Organisation>.None;
    }

    // Case-insensitive for the same reason as FindByOnsCodeAsync: "e06000001" and "E06000001" are the same
    // code, so both must count as a duplicate. The database's unique index is case-sensitive and cannot
    // catch this on its own.
    public async Task<Option<Organisation>> FindDuplicateOnsCodeAsync(string targetOnsCode, OrganisationId sourceId, CancellationToken cancellationToken = default)
    {
        var normalised = targetOnsCode.ToUpper();
        var entity = await Context.Organisations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OnsCode != null && x.OnsCode.ToUpper() == normalised && x.Id != sourceId, cancellationToken);
        return entity ?? Option<Organisation>.None;
    }

    private IQueryable<Organisation> GetQuery(UserRole userRole, Option<OrganisationId> organisationId)
    {
        if (userRole.Equals(UserRole.Admin))
        {
            return Context.Organisations.AsQueryable();
        }

        if (userRole.IsOrganisationMember())
        {
            return organisationId.Match(
                orgId => Context.Organisations.AsQueryable().Where(x => x.Id == orgId),
                Array.Empty<Organisation>().AsQueryable());
        }

        return Array.Empty<Organisation>().AsQueryable();
    }
}