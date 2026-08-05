using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
using Domain.Organisations;
using Domain.OrganisationUsers;
using Domain.ValueObjects;
using FluentAssertions;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Organisations;

// BLUM-7038: an LA Lead may see the users in its own local authority but may not manage them, and may not
// see any other LA's users. Reads sit on the controller's class-level attribute; every mutation narrows to
// Role.UserManagers (admin + organisation admin).
public class OrganisationUsersLeadAccessTests(IntegrationTestWebFactory factory)
    : BaseIntegrationTest(factory), IAsyncLifetime
{
    private readonly Organisation _mainOrg = OrganisationsData.MainOrganisation;
    private readonly Organisation _secondOrg = OrganisationsData.SecondOrganisation;

    private readonly OrganisationUser _mainOrgAdmin =
        OrganisationUsersData.MainOrganisationUser(OrganisationsData.MainOrganisation.Id);

    private readonly OrganisationUser _secondOrgAdmin =
        OrganisationUsersData.SecondOrganisationUser(OrganisationsData.SecondOrganisation.Id);

    private static readonly Guid MainOrgLeadUserId = Guid.Parse("d0000000-0000-0000-0000-000000000001");

    private string MainOrgUsersRoute => $"organisations/{_mainOrg.Id}/users";
    private string SecondOrgUsersRoute => $"organisations/{_secondOrg.Id}/users";

    private void AuthenticateAsMainOrgLead() => AuthenticateAs(Role.Lead, MainOrgLeadUserId, _mainOrg.Id.Value);

    [Fact]
    public async Task ShouldAllowLead_ToListItsOwnOrganisationsUsers()
    {
        AuthenticateAsMainOrgLead();

        var response = await Client.GetAsync(MainOrgUsersRoute);

        response.EnsureSuccessStatusCode();
        var users = await response.ToResponseModel<List<UserDto>>();
        users.Should().ContainSingle(u => u.Email == _mainOrgAdmin.User!.Email);
    }

    [Fact]
    public async Task ShouldAllowLead_ToViewAUserInItsOwnOrganisation()
    {
        AuthenticateAsMainOrgLead();

        var response = await Client.GetAsync($"{MainOrgUsersRoute}/{_mainOrgAdmin.UserId.Value}");

        response.EnsureSuccessStatusCode();
        var user = await response.ToResponseModel<UserDetailDto>();
        user.Email.Should().Be(_mainOrgAdmin.User!.Email);
    }

    [Fact]
    public async Task ShouldNotExposeAnotherOrganisationsUsers_ToLead()
    {
        AuthenticateAsMainOrgLead();

        var listResponse = await Client.GetAsync(SecondOrgUsersRoute);

        // Scoping is applied to the query, not the route, so the other LA's users are simply absent.
        listResponse.EnsureSuccessStatusCode();
        var users = await listResponse.ToResponseModel<List<UserDto>>();
        users.Should().NotContain(u => u.Email == _secondOrgAdmin.User!.Email);

        var detailResponse = await Client.GetAsync($"{SecondOrgUsersRoute}/{_secondOrgAdmin.UserId.Value}");
        detailResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ShouldForbidLead_FromCreatingAUser()
    {
        AuthenticateAsMainOrgLead();

        var response = await Client.PostAsJsonAsync($"{MainOrgUsersRoute}/create", NewUserDto());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ShouldForbidLead_FromEditingAUser()
    {
        AuthenticateAsMainOrgLead();

        var request = NewUserDto() with { Id = _mainOrgAdmin.UserId.Value, Role = Role.OrganisationAdmin };
        var response = await Client.PutAsJsonAsync($"{MainOrgUsersRoute}/edit", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("deactivate")]
    [InlineData("reactivate")]
    [InlineData("reset-mfa")]
    public async Task ShouldForbidLead_FromUserLifecycleActions(string action)
    {
        AuthenticateAsMainOrgLead();

        var response = await Client.PostAsync(
            $"{MainOrgUsersRoute}/{_mainOrgAdmin.UserId.Value}/{action}",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ShouldForbidLead_FromDeletingAUser()
    {
        AuthenticateAsMainOrgLead();

        var response = await Client.DeleteAsync($"{MainOrgUsersRoute}/{_mainOrgAdmin.UserId.Value}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ShouldForbidLead_FromReachingTheDhscUsersSurface()
    {
        AuthenticateAsMainOrgLead();

        var response = await Client.GetAsync("admin/users");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static UserDto NewUserDto() => new(
        Id: null,
        FirstName: "new",
        LastName: "user",
        Email: "lead.created@org.com",
        Status: "active",
        Role: Role.Lead,
        OrganisationName: null);

    public async Task InitializeAsync()
    {
        await Context.Organisations.AddAsync(_mainOrg);
        await Context.Organisations.AddAsync(_secondOrg);
        await Context.OrganisationUsers.AddAsync(_mainOrgAdmin);
        await Context.OrganisationUsers.AddAsync(_secondOrgAdmin);
        await SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        Context.OrganisationUsers.RemoveRange(Context.OrganisationUsers);
        Context.Users.RemoveRange(Context.Users);
        Context.Organisations.RemoveRange(Context.Organisations);
        await SaveChangesAsync();
        AuthenticateAsAdmin();
    }
}