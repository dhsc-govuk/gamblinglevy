using Domain.Locations;
using Domain.Users;
using Location = Domain.Locations.Location;

namespace Domain.LocationUsers;

public class LocationUser : IEntity<LocationUserId>
{
    public LocationUserId Id { get; private set; }
    public UserId UserId { get; private set; }
    public LocationId LocationId { get; private set; }
    public User? User { get; private set; }
    public Location? Location { get; private set; }

    private LocationUser(LocationUserId id, UserId userId, LocationId locationId)
    {
        Id = id;
        UserId = userId;
        LocationId = locationId;
    }

    public static LocationUser New(LocationUserId id, UserId userId, LocationId locationId)
    {
        return new LocationUser(id, userId, locationId);
    }

    public static LocationUser New(LocationUserId id, UserId userId, LocationId locationId, User user)
    {
        var locationUser = New(id, userId, locationId);
        locationUser.User = user;
        return locationUser;
    }
}