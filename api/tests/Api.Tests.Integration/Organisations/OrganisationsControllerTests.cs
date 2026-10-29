using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
using Domain.Organisations;
using FluentAssertions;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Organisations;

public class OrganisationsControllerTests(IntegrationTestWebFactory factory)
    : BaseIntegrationTest(factory), IAsyncLifetime
{
    private readonly Organisation _secondOrganisation = OrganisationsData.SecondOrganisation;

    [Fact]
    public async Task ShouldListLocalAuthorities()
    {
        var response = await Client.GetAsync("organisations/local-authorities");
        response.IsSuccessStatusCode.Should().BeTrue();
        var orgs = await response.ToResponseModel<List<LocalAuthorityDto>>();
        orgs.Should().NotBeNull();
        orgs!.Should().ContainSingle(x => x.Name == _secondOrganisation.Name);
    }

    [Fact]
    public async Task ShouldGetSingleLocalAuthority()
    {
        var response = await Client.GetAsync($"organisations/local-authorities/{_secondOrganisation.Id.Value}");
        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<LocalAuthorityDto>();
        dto.Name.Should().Be(_secondOrganisation.Name);
        dto.OnsCode.Should().Be(_secondOrganisation.OnsCode);
    }

    [Fact]
    public async Task ShouldReturn404ForUnknownLocalAuthority()
    {
        var response = await Client.GetAsync($"organisations/local-authorities/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ShouldListEveryOrganisation()
    {
        // Arrange — a second organisation in a different onboarding state: the list is not filtered by it.
        Context.Organisations.Add(OrganisationsData.MainOrganisation);
        await SaveChangesAsync();

        // Act
        var response = await Client.GetAsync("organisations");

        // Assert — in no promised order.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var organisations = await response.ToResponseModel<List<OrganisationDto>>();

        organisations.Should().BeEquivalentTo(
        [
            new OrganisationDto(OrganisationsData.MainOrganisation.Id.Value, "Main Council", "E06000001", "North East", "LA", "E1000001", "invited"),
            new OrganisationDto(_secondOrganisation.Id.Value, "Second Council", "E06000002", "North West", "LA", "E1000002", "active"),
        ]);
    }

    [Fact]
    public async Task ShouldGetAnOrganisationById()
    {
        // Act
        var response = await Client.GetAsync($"organisations/{_secondOrganisation.Id.Value}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ToResponseModel<OrganisationDto>()).Should().Be(
            new OrganisationDto(_secondOrganisation.Id.Value, "Second Council", "E06000002", "North West", "LA", "E1000002", "active"));
    }

    [Fact]
    public async Task ShouldReturn404_ForAnUnknownOrganisation()
    {
        // Act
        var response = await Client.GetAsync($"organisations/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    public async Task InitializeAsync()
    {
        Context.Organisations.Add(_secondOrganisation);
        await SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        Context.OrgFundingAllocations.RemoveRange(Context.OrgFundingAllocations);
        Context.FundingWindows.RemoveRange(Context.FundingWindows);
        Context.Organisations.RemoveRange(Context.Organisations);
        await SaveChangesAsync();
    }
}