using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Domain.Collections;
using Domain.Organisations;
using LanguageExt;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class CollectionRepository(ApplicationDbContext dbContext)
    : RepositoryBase<Collection, CollectionId>(dbContext), ICollectionRepository, ICollectionQueries
{
    Task ICollectionRepository.UpdateAsync(Collection collection, CancellationToken cancellationToken)
        => UpdateAsync(collection, cancellationToken);

    public async Task<Option<Collection>> GetByIdAsync(
        CollectionId id,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        // Tracked load: command handlers mutate the aggregate (incl. replacing owned Forms/LocalAuthorities/
        // Reminders) and rely on the change tracker to diff added/removed children on save.
        var query = WithGraph(Context.Collections)
            .Where(x => x.Id == id);

        if (!includeDeleted)
        {
            query = query.Where(x => x.DeletedAt == null);
        }

        var entity = await query.FirstOrDefaultAsync(cancellationToken);
        return entity is null ? Option<Collection>.None : Option<Collection>.Some(entity);
    }

    public async Task<IReadOnlyList<Collection>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await WithGraph(Context.Collections.AsNoTracking())
            .Where(x => x.DeletedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Collection>> GetDeletedAsync(CancellationToken cancellationToken = default)
    {
        return await WithGraph(Context.Collections.AsNoTracking())
            .Where(x => x.DeletedAt != null)
            .OrderByDescending(x => x.DeletedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ExistsByNameAsync(
        string name,
        CollectionId? excludingId,
        CancellationToken cancellationToken = default)
    {
        var normalised = (name ?? string.Empty).Trim().ToLower();

        var query = Context.Collections
            .AsNoTracking()
            .Where(x => x.DeletedAt == null && x.Name.ToLower() == normalised);

        if (excludingId is not null)
        {
            // Compare against a non-nullable CollectionId so EF can translate the value-object inequality
            // (a nullable CollectionId? operand is not translatable).
            CollectionId excluded = excludingId;
            query = query.Where(x => x.Id != excluded);
        }

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Collection>> GetAssignedToOrganisationAsync(
        OrganisationId organisationId,
        CancellationToken cancellationToken = default)
    {
        // Coarse prefilter for the LA-facing surface: non-deleted collections this org could reach, either by an
        // explicit assignment row or by the "submitted by all LAs" flag (persisted with NO
        // collection_local_authorities rows). Callers MUST still apply SubmissionEligibility.IsAssignedTo, the
        // authoritative type-aware gate — all-LAs only counts for Local-Authority orgs, which can't be checked here
        // without translating an OrgType value object into SQL. IsSubmittedByAllLas is nullable; "== true" excludes null/false drafts.
        return await WithGraph(Context.Collections.AsNoTracking())
            .Where(x => x.DeletedAt == null
                && (x.IsSubmittedByAllLas == true
                    || x.LocalAuthorities.Any(la => la.LocalAuthorityId == organisationId)))
            .OrderBy(x => x.SubmissionDeadline)
            .ThenBy(x => x.CollectionOpenDate)
            .ThenBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Collection>> GetSubmittedByOrganisationAsync(
        OrganisationId organisationId,
        CancellationToken cancellationToken = default)
    {
        // Non-deleted collections the org has any submission for, in one EXISTS query — no Contains over a list of
        // value-object ids (which would not translate) and no N+1. Scalar equalities only.
        return await WithGraph(Context.Collections.AsNoTracking())
            .Where(c => c.DeletedAt == null
                && Context.Submissions.Any(s => s.CollectionId == c.Id && s.OrganisationId == organisationId))
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<Collection> WithGraph(IQueryable<Collection> query)
        => query
            .Include(c => c.Forms)
            .Include(c => c.LocalAuthorities)
            .Include(c => c.Reminders);
}