using System.Net.Http.Json;
using Api.Dtos;
using Domain.Organisations;
using Domain.OrganisationUsers;
using Domain.Users;
using Domain.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Organisations;

public class OrganisationUsersControllerTests : BaseIntegrationTest, IAsyncLifetime
{
    private readonly Organisation _mainOrganisation = OrganisationsData.MainOrganisation;

    private readonly OrganisationUser _mainOrganisationAdmin;
    private readonly OrganisationUser _secondOrganisationAdmin;

    private readonly string _mainCreateOrganisationUserRoute;
    private readonly string _mainUpdateOrganisationUserRoute;

    public OrganisationUsersControllerTests(IntegrationTestWebFactory factory) : base(factory)
    {
        _mainOrganisationAdmin = OrganisationUsersData.MainOrganisationUser(_mainOrganisation.Id);
        _secondOrganisationAdmin = OrganisationUsersData.SecondOrganisationUser(_mainOrganisation.Id);

        _mainCreateOrganisationUserRoute = $"organisations/{_mainOrganisation.Id}/users/create";
        _mainUpdateOrganisationUserRoute = $"organisations/{_mainOrganisation.Id}/users/edit";
    }

    [Fact]
    public async Task ShouldCreateOrganisationAdmin()
    {
        // Arrange
        var request = new UserDto(
            Id: null,
            FirstName: _mainOrganisationAdmin.User!.Name.FirstName,
            LastName: _mainOrganisationAdmin.User.Name.LastName,
            Email: _mainOrganisationAdmin.User.Email,
            Status: _mainOrganisationAdmin.User.Status.ToString(),
            Role: _mainOrganisationAdmin.User.Role.ToString(),
            OrganisationName: null);

        // Act
        var response = await Client.PostAsJsonAsync(_mainCreateOrganisationUserRoute, request);

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();
        var organisationAdmin = await response.ToResponseModel<UserDto>();
        var dbOrganisationAdmin = await Context.OrganisationUsers
            .AsNoTracking()
            .Include(x => x.User)
            .FirstAsync(x => x.Id == new OrganisationUserId(organisationAdmin.Id!.Value));
        dbOrganisationAdmin.User!.Name.FirstName.Should().Be(request.FirstName);
        dbOrganisationAdmin.User.Name.LastName.Should().Be(request.LastName);
        dbOrganisationAdmin.User.Email.Should().Be(request.Email);
        dbOrganisationAdmin.User.Status.ToString().Should().Be(request.Status.ToString());
        dbOrganisationAdmin.User.Role.Should().Be(UserRole.OrganisationAdmin);
    }

    [Fact]
    public async Task ShouldUpdateOrganisationAdmin()
    {
        // Arrange
        var request = new UserDto(
            Id: _secondOrganisationAdmin.Id.Value,
            FirstName: _mainOrganisationAdmin.User!.Name.FirstName,
            LastName: _mainOrganisationAdmin.User.Name.LastName,
            Email: _mainOrganisationAdmin.User.Email,
            Status: _mainOrganisationAdmin.User.Status.ToString(),
            Role: _mainOrganisationAdmin.User.Role.ToString(),
            OrganisationName: null);

        // Act
        var response = await Client.PutAsJsonAsync(_mainUpdateOrganisationUserRoute, request);

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();
        var organisationAdmin = await response.ToResponseModel<UserDto>();
        var dbOrganisationAdmin = await Context.OrganisationUsers
            .AsNoTracking()
            .Include(x => x.User)
            .FirstAsync(x => x.UserId == new UserId(organisationAdmin.Id!.Value));
        dbOrganisationAdmin.User!.Name.FirstName.Should().Be(request.FirstName);
        dbOrganisationAdmin.User.Name.LastName.Should().Be(request.LastName);
        dbOrganisationAdmin.User.Email.Should().Be(request.Email);
        dbOrganisationAdmin.User.Status.ToString().Should().Be(request.Status.ToString());
        dbOrganisationAdmin.User.Role.Should().Be(UserRole.OrganisationAdmin);
    }

    public async Task InitializeAsync()
    {
        await Context.Organisations.AddAsync(_mainOrganisation);
        await Context.OrganisationUsers.AddAsync(_secondOrganisationAdmin);

        await SaveChangesAsync();
        Context.ChangeTracker.Clear();
    }

    public async Task DisposeAsync()
    {
        Context.OrganisationUsers.RemoveRange(Context.OrganisationUsers);
        Context.Users.RemoveRange(Context.Users);
        Context.Organisations.RemoveRange(Context.Organisations);

        await SaveChangesAsync();
    }
}