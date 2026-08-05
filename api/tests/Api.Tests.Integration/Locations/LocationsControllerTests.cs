using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
using Domain.Locations;
using Domain.Organisations;
using FluentAssertions;
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
        Context.Locations.RemoveRange(Context.Locations);
        Context.Organisations.RemoveRange(Context.Organisations);

        await SaveChangesAsync();
    }
}