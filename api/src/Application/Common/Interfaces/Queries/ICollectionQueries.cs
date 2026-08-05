using Domain.Collections;
using Domain.Organisations;
using LanguageExt;

namespace Application.Common.Interfaces.Queries;

public interface ICollectionQueries
{
    // Organisation-wide load used by mutation handlers and the detail screen. By default excludes soft-deleted
    // records; pass includeDeleted to load a deleted record (e.g. for restore).
    Task<Option<Collection>> GetByIdAsync(CollectionId id, bool includeDeleted = false, CancellationToken cancellationToken = default);

    // All non-deleted collections — feeds the collections list screen, which groups by derived status client-side.
    Task<IReadOnlyList<Collection>> GetAllAsync(CancellationToken cancellationToken = default);

    // Soft-deleted collections — feeds the "Deleted collections" page.
    Task<IReadOnlyList<Collection>> GetDeletedAsync(CancellationToken cancellationToken = default);

    // Uniqueness guard for the name field. When updating, pass the current id to excludingId so a record does not
    // clash with itself.
    Task<bool> ExistsByNameAsync(string name, CollectionId? excludingId, CancellationToken cancellationToken = default);

    // Non-deleted collections the organisation is assigned to (collection_local_authorities). Status filtering
    // happens in memory at the call site — CollectionStatus is derived via GetStatus(), not a column.
    Task<IReadOnlyList<Collection>> GetAssignedToOrganisationAsync(OrganisationId organisationId, CancellationToken cancellationToken = default);

    // Non-deleted collections the organisation has at least one submission for, in a single read. Feeds the LA
    // submission-history union so a window the org submitted to but is no longer assigned to is included without an
    // N+1 per-collection lookup. AsNoTracking — read-only.
    Task<IReadOnlyList<Collection>> GetSubmittedByOrganisationAsync(OrganisationId organisationId, CancellationToken cancellationToken = default);
}