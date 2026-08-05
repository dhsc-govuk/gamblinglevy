using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
using FluentAssertions;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.Activities;

// Org-scoping: an LA only ever sees its own activity records. The role-gating tests that lived here went
// with the view-only roles they covered (BLUM-7038) — every surviving LA role may both read and mutate.
public class ActivityAccessTests(IntegrationTestWebFactory factory) : ActivityTestBase(factory)
{
    [Fact]
    public async Task ShouldNotExposeAnotherOrganisationsActivity()
    {
        AuthenticateAsMainOrgLead();
        var created = await CreatePlannedActivityAsync();

        AuthenticateAsSecondOrgLead();

        var getResponse = await Client.GetAsync($"activities/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var updateResponse = await Client.PutAsJsonAsync(
            $"activities/{created.Id}",
            new UpdateActivityRequestDto("Hijack", Guid.NewGuid(), Guid.NewGuid(), null, "planned", null, null, false));
        updateResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var listResponse = await Client.GetAsync("activities");
        listResponse.EnsureSuccessStatusCode();
        var list = await listResponse.ToResponseModel<List<ActivitySummaryDto>>();
        list.Should().BeEmpty();
    }
}