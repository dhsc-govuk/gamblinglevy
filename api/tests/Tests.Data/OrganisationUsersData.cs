using Domain.Organisations;
using Domain.OrganisationUsers;
using Domain.Users;
using Domain.ValueObjects;

namespace Tests.Data;

// OrganisationUserId is deliberately derived from the user id: every OrganisationUsers command reads one
// route guid as both the OrganisationUserId and the UserId, and UserDto surfaces the org-user id back to
// the client as the user id, so id == user_id is a system invariant (BLUM-7038). Seeding a synthetic id
// here would model data production can no longer produce, and GET users/{userId} would 404 against it.
public static class OrganisationUsersData
{
    public static OrganisationUser MainOrganisationUser(OrganisationId organisationId)
    {
        var user = User.New(
            id: UserId.New(),
            name: new Name("main organisation", "admin"),
            email: "main.organisation.admin@org.com",
            new SubId(Guid.NewGuid()),
            status: UserStatus.Active,
            role: UserRole.OrganisationAdmin);

        return OrganisationUser.New(
            id: new OrganisationUserId(user.Id.Value),
            userId: user.Id,
            organisationId: organisationId,
            user: user);
    }

    public static OrganisationUser SecondOrganisationUser(OrganisationId organisationId)
    {
        var user = User.New(
            id: UserId.New(),
            name: new Name("second organisation", "admin"),
            email: "second.organisation.admin@org.com",
            new SubId(Guid.NewGuid()),
            status: UserStatus.Active,
            role: UserRole.OrganisationAdmin);

        return OrganisationUser.New(
            id: new OrganisationUserId(user.Id.Value),
            userId: user.Id,
            organisationId: organisationId,
            user: user);
    }
}