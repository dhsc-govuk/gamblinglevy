using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
using Domain.AuditLogs;
using Domain.Locations;
using Domain.Organisations;
using Domain.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Locations;

public class LocationsControllerTests : BaseIntegrationTest, IAsyncLifetime
{
    private readonly Organisation _mainOrganisation = OrganisationsData.MainOrganisation;
    private readonly Location _mainLocation;
    private readonly Location _secondLocation;

    private readonly string _mainCreateLocationRoute;
    private readonly string _mainUpdateLocationRoute;

    public LocationsControllerTests(IntegrationTestWebFactory factory) : base(factory)
    {
        _mainLocation = LocationsData.MainLocation(_mainOrganisation.Id);
        _secondLocation = LocationsData.SecondLocation(_mainOrganisation.Id);

        _mainCreateLocationRoute = $"organisations/{_mainOrganisation.Id}/locations/create";
        _mainUpdateLocationRoute = $"organisations/{_mainOrganisation.Id}/locations/edit";
    }

    [Fact]
    public async Task ShouldCreateLocation()
    {
        // Arrange
        var request = new LocationDto(
            Id: null,
            OrganisationId: _mainOrganisation.Id.Value,
            Name: _mainLocation.Name,
            IsActive: _mainLocation.IsActive);

        // Act
        var response = await Client.PostAsJsonAsync(_mainCreateLocationRoute, request);

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();
        var location = await response.ToResponseModel<LocationDto>();
        Context.Locations.Any(x => x.Id == new LocationId(location.Id!.Value)).Should().BeTrue();
    }

    // BLUM-8747: creating a location is audited against the location, scoped to its organisation and attributed to
    // the caller. Run as the LA's own admin, one of the two roles LocationsController admits.
    [Fact]
    public async Task ShouldCreateLocation_AndWriteOneCreatedAuditRowForItsOrganisation()
    {
        var laAdminUserId = Guid.Parse("c0000000-0000-0000-0000-0000000000a1");
        AuthenticateAs(Role.OrganisationAdmin, laAdminUserId, _mainOrganisation.Id.Value);

        var location = await CreateAuditedLocationAsync();

        var row = await SingleLocationCreatedRowAsync();
        row.TargetType.Should().Be(AuditTargetType.Location);
        row.TargetId.Should().Be(location.Id!.Value);
        row.OrganisationId.Should().Be(_mainOrganisation.Id);
        row.ActorUserId.Value.Should().Be(laAdminUserId);
        row.After.Should().Contain("Audited location");
    }

    // The other role LocationsController admits is the DHSC admin. PermissionsService.AdminPermission carries no
    // UserId, so CreateLocationCommandHandler's `permission.UserId.IfNone(UserId.Empty())` records the DHSC admin's
    // change with an empty actor (UpdateLocationCommandHandler has the same fallback). Un-skip once the handlers
    // take the actor from PermissionsService.GetUserId() instead.
    [Fact]
    public async Task ShouldAttributeTheLocationCreatedAuditRowToTheDhscAdmin()
    {
        await CreateAuditedLocationAsync();

        var row = await SingleLocationCreatedRowAsync();
        row.ActorUserId.Should().Be(AdminsData.MainAdmin.Id);
    }

    private async Task<LocationDto> CreateAuditedLocationAsync()
    {
        var request = new LocationDto(
            Id: null,
            OrganisationId: _mainOrganisation.Id.Value,
            Name: "Audited location",
            IsActive: true);

        var response = await Client.PostAsJsonAsync(_mainCreateLocationRoute, request);

        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        return await response.ToResponseModel<LocationDto>();
    }

    private async Task<Domain.AuditLogs.AuditLog> SingleLocationCreatedRowAsync()
    {
        var rows = await Context.AuditLogs.AsNoTracking()
            .Where(a => a.Action == AuditAction.LocationCreated)
            .ToListAsync();
        return rows.Should().ContainSingle().Subject;
    }

    [Fact]
    public async Task ShouldNotCreateLocationBecauseNameDuplication()
    {
        // Arrange
        var request = new LocationDto(
            Id: null,
            OrganisationId: _mainOrganisation.Id.Value,
            Name: _secondLocation.Name,
            IsActive: _secondLocation.IsActive);

        // Act
        var response = await Client.PostAsJsonAsync(_mainCreateLocationRoute, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ShouldUpdateLocation()
    {
        // Arrange
        const string newLocationName = "New location name";
        var request = new LocationDto(
            Id: _secondLocation.Id.Value,
            OrganisationId: _mainOrganisation.Id.Value,
            Name: newLocationName,
            IsActive: _secondLocation.IsActive);

        // Act
        var response = await Client.PutAsJsonAsync(_mainUpdateLocationRoute, request);

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();
        var location = await response.ToResponseModel<LocationDto>();
        Context.Locations.First(x => x.Id == new LocationId(location.Id!.Value))
            .Name.Should().Be(newLocationName);
    }

    public async Task InitializeAsync()
    {
        Context.Organisations.Add(_mainOrganisation);
        Context.Locations.Add(_secondLocation);

        await SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        Context.Locations.RemoveRange(Context.Locations);
        Context.Organisations.RemoveRange(Context.Organisations);

        await SaveChangesAsync();
        AuthenticateAsAdmin();
    }
}