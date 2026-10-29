using Domain.Locations;
using Domain.LocationUsers;
using Domain.Users;
using Domain.ValueObjects;

namespace Tests.Data;

// The dedicated 'location admin' role went away with BLUM-7038, so these users carry the surviving Lead
// role. A location_users row owned by a Lead is semantically odd but structurally legal, and the only
// remaining consumer (ShouldHardDeleteLocationUser_AndClearJoinRow) exercises the RESTRICT foreign key on
// user deletion, not role behaviour.
public static class LocationUsersData
{
    public static LocationUser MainLocationUser(LocationId locationId)
    {
        var user = User.New(
            id: UserId.New(),
            name: new Name("main location", "user"),
            email: "main.location.admin@loc.com",
            new SubId(Guid.NewGuid()),
            status: UserStatus.Active,
            role: UserRole.Lead,
            utcNow: DateTime.UtcNow);

        return LocationUser.New(id: LocationUserId.New(), userId: user.Id, locationId: locationId, user: user);
    }

    public static LocationUser SecondLocationUser(LocationId locationId)
    {
        var user = User.New(
            id: UserId.New(),
            name: new Name("second location", "user"),
            email: "second.location.admin2@loc.com",
            new SubId(Guid.NewGuid()),
            status: UserStatus.Deactivated,
            role: UserRole.Lead,
            utcNow: DateTime.UtcNow);

        return LocationUser.New(id: LocationUserId.New(), userId: user.Id, locationId: locationId, user: user);
    }
}