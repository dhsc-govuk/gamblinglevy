using Domain.Common;
using Domain.Organisations;

namespace Domain.Locations;

public class Location : IEntity<LocationId>
{
    public LocationId Id { get; private set; }
    public OrganisationId OrganisationId { get; private set; }
    public string Name { get; private set; }
    public bool IsActive { get; private set; }
    public Organisation? Organisation { get; private set; }

    private Location(LocationId id, OrganisationId organisationId, string name, bool isActive)
    {
        Guard.NotNullOrEmptyOrWhiteSpace(name, nameof(name));
        Id = id;
        OrganisationId = organisationId;
        Name = name;
        IsActive = isActive;
    }

    public static Location New(LocationId id, OrganisationId organisationId, string name, bool isActive)
    {
        return new Location(id, organisationId, name, isActive);
    }

    public void UpdateDetails(string name, bool isActive)
    {
        Guard.NotNullOrEmptyOrWhiteSpace(name, nameof(name));
        Name = name;
        IsActive = isActive;
    }
}