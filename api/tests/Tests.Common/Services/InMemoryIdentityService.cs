using Application.Common.Interfaces;
using Domain.Locations;
using Domain.Organisations;
using Domain.Users;
using Domain.ValueObjects;
using LanguageExt;
using Tests.Data;

namespace Tests.Common.Services;

public class InMemoryIdentityService : IIdentityService
{
    public Option<UserId> GetUserId()
    {
        return new UserId(TestIdentityContext.Current.UserId);
    }

    public Option<SubId> GetSubId()
    {
        throw new NotImplementedException();
    }

    public Option<UserRole> GetRole()
    {
        return UserRole.From(TestIdentityContext.Current.Role);
    }

    public Option<OrganisationId> GetOrganisationId()
    {
        var organisationId = TestIdentityContext.Current.OrganisationId;
        return organisationId.HasValue
            ? new OrganisationId(organisationId.Value)
            : Option<OrganisationId>.None;
    }

    public Option<LocationId> GetLocationId()
    {
        return Option<LocationId>.None;
    }

    public Option<string> GetEmail()
    {
        return AdminsData.MainAdmin.Email;
    }
}