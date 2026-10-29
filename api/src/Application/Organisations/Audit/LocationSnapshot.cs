using System.Text.Json;
using Domain.Locations;

namespace Application.Organisations.Audit;

internal static class LocationSnapshot
{
    public static string Serialize(Location location)
    {
        return JsonSerializer.Serialize(new
        {
            id = location.Id.Value,
            organisationId = location.OrganisationId.Value,
            name = location.Name,
            isActive = location.IsActive,
        });
    }
}