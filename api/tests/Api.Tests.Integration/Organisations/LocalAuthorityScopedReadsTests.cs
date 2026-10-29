using System.Net;
using Api.Dtos;
using Domain.Locations;
using Domain.Organisations;
using Domain.OrganisationUsers;
using Domain.ValueObjects;
using FluentAssertions;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Organisations;

/// <summary>
/// BLUM-8761 — the read endpoints that answer from "the caller's organisation": the three /totals routes an
/// LA or DHSC can reach, and the Locations reads. None of them had a test, and all of them are scoped by the
/// same thing: PermissionScopeExtensions.ScopedTo, the one role switch every scoped repository goes through
/// (BLUM-8751), which throws for a role it cannot scope rather than answering with nothing.
///
/// Two local authorities, each with its own members, locations and location users, in different numbers, so
/// a query that lost its scope returns a total that belongs to neither.
/// </summary>
public class LocalAuthorityScopedReadsTests(IntegrationTestWebFactory factory)
    : BaseIntegrationTest(factory), IAsyncLifetime
{
    private readonly Organisation _mainOrg = OrganisationsData.MainOrganisation;
    private readonly Organisation _secondOrg = OrganisationsData.SecondOrganisation;

    private readonly OrganisationUser _mainAdmin =
        OrganisationUsersData.MainOrganisationUser(OrganisationsData.MainOrganisation.Id);

    private readonly OrganisationUser _secondAdmin =
        OrganisationUsersData.SecondOrganisationUser(OrganisationsData.SecondOrganisation.Id);

    private readonly Location _mainLocation = LocationsData.MainLocation(OrganisationsData.MainOrganisation.Id);

    private readonly Location _closedDepot =
        Location.New(LocationId.New(), OrganisationsData.MainOrganisation.Id, "Closed depot", isActive: false);

    private readonly Location _secondLocation = LocationsData.SecondLocation(OrganisationsData.SecondOrganisation.Id);

    [Theory]
    [InlineData(Role.OrganisationAdmin)]
    [InlineData(Role.Lead)]
    public async Task ShouldReturnHomeTotals_ForTheCallersOwnOrganisationOnly(string role)
    {
        // Act
        AuthenticateAs(role, Guid.NewGuid(), _mainOrg.Id.Value);
        var main = await Client.GetAsync("organisation-admin/totals");

        AuthenticateAs(role, Guid.NewGuid(), _secondOrg.Id.Value);
        var second = await Client.GetAsync("organisation-admin/totals");

        // Assert — "Admins" is every member of the LA whatever their role or status, and "Locations" counts
        // inactive ones too. The route takes no organisation, so the totals can only come from the token.
        main.StatusCode.Should().Be(HttpStatusCode.OK);
        (await main.ToResponseModel<OrganisationAdminHomeDto>()).Should().Be(new OrganisationAdminHomeDto(Admins: 3, Locations: 2));

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await second.ToResponseModel<OrganisationAdminHomeDto>()).Should().Be(new OrganisationAdminHomeDto(Admins: 1, Locations: 1));
    }

    [Fact]
    public async Task ShouldForbidDhscAdmin_FromTheOrganisationHomeTotals()
    {
        AuthenticateAsAdmin();

        // Act
        var response = await Client.GetAsync("organisation-admin/totals");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ShouldReturnTotals_ForTheRequestedOrganisation()
    {
        AuthenticateAsAdmin();

        // Act
        var main = await Client.GetAsync($"organisations/{_mainOrg.Id.Value}/totals");
        var second = await Client.GetAsync($"organisations/{_secondOrg.Id.Value}/totals");

        // Assert
        main.StatusCode.Should().Be(HttpStatusCode.OK);
        (await main.ToResponseModel<OrganisationHomeDto>()).Should().Be(new OrganisationHomeDto(_mainOrg.Name, Admins: 3, Locations: 2));

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await second.ToResponseModel<OrganisationHomeDto>()).Should().Be(new OrganisationHomeDto(_secondOrg.Name, Admins: 1, Locations: 1));
    }

    [Fact]
    public async Task ShouldReturn404_ForTotalsOfAnUnknownOrganisation()
    {
        AuthenticateAsAdmin();

        // Act
        var response = await Client.GetAsync($"organisations/{Guid.NewGuid()}/totals");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ShouldListTheLocations_OfTheRequestedOrganisation()
    {
        AuthenticateAsAdmin();

        // Act
        var response = await Client.GetAsync(LocationsRoute(_mainOrg));

        // Assert — inactive locations are listed too, and the list carries no nested organisation.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var locations = await response.ToResponseModel<List<LocationDto>>();

        locations.Should().BeEquivalentTo(
        [
            new LocationDto(_mainLocation.Id.Value, _mainOrg.Id.Value, "Main", IsActive: true),
            new LocationDto(_closedDepot.Id.Value, _mainOrg.Id.Value, "Closed depot", IsActive: false),
        ]);
    }

    [Fact]
    public async Task ShouldReturnAnEmptyList_WhenAnOrganisationAdminAsksForAnotherOrganisationsLocations()
    {
        AuthenticateAs(Role.OrganisationAdmin, _mainAdmin.UserId.Value, _mainOrg.Id.Value);

        // Act
        var own = await Client.GetAsync(LocationsRoute(_mainOrg));
        var other = await Client.GetAsync(LocationsRoute(_secondOrg));

        // Assert — the route organisation only narrows what the token already allows, so another LA's route
        // answers with an empty list rather than a refusal. That is the same answer an organisation that
        // does not exist gets, so it says nothing about whether the other LA exists or has locations
        // (OrganisationUsersLeadAccessTests pins the same behaviour for users).
        own.StatusCode.Should().Be(HttpStatusCode.OK);
        (await own.ToResponseModel<List<LocationDto>>()).Should().HaveCount(2);

        other.StatusCode.Should().Be(HttpStatusCode.OK);
        (await other.ToResponseModel<List<LocationDto>>()).Should().BeEmpty();
    }

    [Theory]
    [InlineData(Role.Admin)]
    [InlineData(Role.OrganisationAdmin)]
    public async Task ShouldGetALocation_WithItsOrganisation(string role)
    {
        AuthenticateInMainOrganisationAs(role);

        // Act
        var response = await Client.GetAsync($"{LocationsRoute(_mainOrg)}/{_closedDepot.Id.Value}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var location = await response.ToResponseModel<LocationDto>();

        location.Id.Should().Be(_closedDepot.Id.Value);
        location.OrganisationId.Should().Be(_mainOrg.Id.Value);
        location.Name.Should().Be("Closed depot");
        location.IsActive.Should().BeFalse();
        location.Organisation!.Id.Should().Be(_mainOrg.Id.Value);
        location.Organisation.Name.Should().Be(_mainOrg.Name);
    }

    [Theory]
    [InlineData(Role.Admin)]
    [InlineData(Role.OrganisationAdmin)]
    public async Task ShouldReturnLocationTotals_CountingOnlyThatLocationsUsers(string role)
    {
        AuthenticateInMainOrganisationAs(role);

        // Act
        var response = await Client.GetAsync($"{LocationsRoute(_mainOrg)}/{_mainLocation.Id.Value}/totals");

        // Assert — one user at this location. The second LA's location has one as well, so a count that
        // was not narrowed to the location answers 2 for DHSC.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ToResponseModel<LocationHomeDto>()).Should().Be(new LocationHomeDto("Main", _mainOrg.Name, Admins: 1));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("", false)]
    [InlineData("/totals", true)]
    [InlineData("/totals", false)]
    public async Task ShouldNotExposeAnotherOrganisationsLocation(string suffix, bool routeNamesTheLocationsOrganisation)
    {
        AuthenticateAs(Role.OrganisationAdmin, _secondAdmin.UserId.Value, _secondOrg.Id.Value);

        // Act — detail and totals ignore the organisation in the route, so neither the location's own
        // organisation nor the caller's gets an LA admin to another LA's location.
        var routeOrganisation = routeNamesTheLocationsOrganisation ? _mainOrg : _secondOrg;
        var response = await Client.GetAsync($"{LocationsRoute(routeOrganisation)}/{_mainLocation.Id.Value}{suffix}");

        // Assert — 404, the same as a location that does not exist.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().NotContain(_mainOrg.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/{0}")]
    [InlineData("/{0}/totals")]
    public async Task ShouldForbidLead_FromTheLocationsSurface(string suffix)
    {
        // Arrange — Lead is an LA role, but the Locations controller names admin and LA admin only.
        AuthenticateAs(Role.Lead, Guid.NewGuid(), _mainOrg.Id.Value);

        // Act
        var response = await Client.GetAsync(LocationsRoute(_mainOrg) + string.Format(suffix, _mainLocation.Id.Value));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/totals")]
    public async Task ShouldReturn404_ForAnUnknownLocation(string suffix)
    {
        AuthenticateAsAdmin();

        // Act
        var response = await Client.GetAsync($"{LocationsRoute(_mainOrg)}/{Guid.NewGuid()}{suffix}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    public async Task InitializeAsync()
    {
        await Context.Organisations.AddRangeAsync(_mainOrg, _secondOrg);
        await SaveChangesAsync();

        await Context.OrganisationUsers.AddRangeAsync(
            _mainAdmin,
            OrganisationUsersData.Member(_mainOrg.Id, UserRole.Lead, "lead@main-council.gov.uk"),
            OrganisationUsersData.Member(_mainOrg.Id, UserRole.Lead, "former.lead@main-council.gov.uk", UserStatus.Deactivated),
            _secondAdmin);
        await Context.Locations.AddRangeAsync(_mainLocation, _closedDepot, _secondLocation);
        await SaveChangesAsync();

        await Context.LocationUsers.AddRangeAsync(
            LocationUsersData.MainLocationUser(_mainLocation.Id),
            LocationUsersData.SecondLocationUser(_secondLocation.Id));
        await SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        // location_users restricts the delete of its user, so the join rows go first.
        Context.LocationUsers.RemoveRange(Context.LocationUsers);
        Context.OrganisationUsers.RemoveRange(Context.OrganisationUsers);
        await SaveChangesAsync();

        Context.Users.RemoveRange(Context.Users);
        Context.Locations.RemoveRange(Context.Locations);
        Context.Organisations.RemoveRange(Context.Organisations);
        await SaveChangesAsync();
        AuthenticateAsAdmin();
    }

    // DHSC carries no organisation, so it is the default identity rather than an admin role with an
    // organisation claim attached, which is a principal production never issues.
    private void AuthenticateInMainOrganisationAs(string role)
    {
        if (role == Role.Admin)
        {
            AuthenticateAsAdmin();
            return;
        }

        AuthenticateAs(role, _mainAdmin.UserId.Value, _mainOrg.Id.Value);
    }

    private static string LocationsRoute(Organisation organisation) => $"organisations/{organisation.Id.Value}/locations";
}