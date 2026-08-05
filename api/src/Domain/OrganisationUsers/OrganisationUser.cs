using Domain.Organisations;
using Domain.Users;

namespace Domain.OrganisationUsers;

public class OrganisationUser : IEntity<OrganisationUserId>
{
    public OrganisationUserId Id { get; }
    public UserId UserId { get; private set; }
    public OrganisationId OrganisationId { get; private set; }
    public User? User { get; private set; }
    public Organisation? Organisation { get; }

    private OrganisationUser(OrganisationUserId id, UserId userId, OrganisationId organisationId)
    {
        Id = id;
        UserId = userId;
        OrganisationId = organisationId;
    }

    public static OrganisationUser New(
        OrganisationUserId id,
        UserId userId,
        OrganisationId organisationId,
        User user)
        => new(id, userId, organisationId) { User = user };
}