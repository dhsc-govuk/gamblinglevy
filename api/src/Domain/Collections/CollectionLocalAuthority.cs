using Domain.Common;
using Domain.Organisations;

namespace Domain.Collections;

// Join row assigning a Local Authority (an Organisation row) to a collection. Modelled as an id-only
// reference satellite so it persists through the Collection aggregate without EF tracking conflicts.
public class CollectionLocalAuthority : IEntity<CollectionLocalAuthorityId>
{
    public CollectionLocalAuthorityId Id { get; private set; }
    public CollectionId CollectionId { get; private set; }
    public OrganisationId LocalAuthorityId { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private CollectionLocalAuthority(
        CollectionLocalAuthorityId id,
        CollectionId collectionId,
        OrganisationId localAuthorityId,
        DateTime createdAt)
    {
        Id = id;
        CollectionId = collectionId;
        LocalAuthorityId = localAuthorityId;
        CreatedAt = createdAt;
    }

    public static CollectionLocalAuthority Create(
        CollectionLocalAuthorityId id,
        CollectionId collectionId,
        OrganisationId localAuthorityId,
        DateTime utcNow)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNull(collectionId, nameof(collectionId));
        Guard.NotNull(localAuthorityId, nameof(localAuthorityId));

        return new CollectionLocalAuthority(id, collectionId, localAuthorityId, utcNow);
    }
}