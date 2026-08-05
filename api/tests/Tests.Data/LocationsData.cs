using Domain.Locations;
using Domain.Organisations;

namespace Tests.Data;

public static class LocationsData
{
    public static Location MainLocation(OrganisationId organisationId) => Location.New(
        id: LocationId.New(),
        organisationId: organisationId,
        name: "Main",
        isActive: true);

    public static Location SecondLocation(OrganisationId organisationId) => Location.New(
        id: LocationId.New(),
        organisationId: organisationId,
        name: "Second Location",
        isActive: true);
}