using Domain.Organisations;

namespace Tests.Data;

public static class OrganisationsData
{
    public static readonly Organisation MainOrganisation = Organisation.New(
        id: OrganisationId.New(),
        name: "Main Council",
        onsCode: "E06000001",
        region: Region.NorthEast,
        type: OrgType.LocalAuthority,
        eCode: "E1000001",
        onboardingStatus: OnboardingStatus.Invited);

    public static readonly Organisation SecondOrganisation = Organisation.New(
        id: OrganisationId.New(),
        name: "Second Council",
        onsCode: "E06000002",
        region: Region.NorthWest,
        type: OrgType.LocalAuthority,
        eCode: "E1000002",
        onboardingStatus: OnboardingStatus.Active);
}