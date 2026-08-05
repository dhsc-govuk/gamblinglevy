using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
using Domain.Activities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.Activities;

// Per-category minimum key-result selections on completion (Question Protocol 3 July, BLUM-6933): types with
// standard options need 1, except needs assessment which keeps "select at least 2"; the "Other type of ..."
// free-text screens still need 2 custom entries. Drafts are never checked.
public class ActivityMinimumKeyResultsTests(IntegrationTestWebFactory factory) : ActivityTestBase(factory)
{
    private const string SteeringGroupTaskName = "Setting up and running a local steering group";
    private const string RnaTaskName = "Contributing to a regional needs assessment";
    private const string OtherStrategyTaskName = "Other type of strategy or leadership";

    // The seeded (category, type) pair plus the type's first standard key-result code.
    private async Task<(Guid CategoryId, Guid TaskId, string KeyResultCode)> GetTypeWithStandardResultAsync(string taskName)
    {
        var taxonomy = await GetTaxonomyAsync();
        var task = taxonomy.Tasks.First(t => t.Name == taskName);
        var keyResult = taxonomy.KeyResults.First(k => k.TaskId == task.Id);
        return (task.CategoryId, task.Id, keyResult.Code);
    }

    [Fact]
    public async Task ShouldCompleteActivity_WithOneStandardKeyResult_WhenNotNeedsAssessment()
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, taskId, keyResultCode) = await GetTypeWithStandardResultAsync(SteeringGroupTaskName);

        var request = new CreateActivityRequestDto(
            "Steering group", categoryId, taskId, null, "planned", null, null, false, [new ExpectedResultInputDto(keyResultCode, null)]);
        var response = await Client.PostAsJsonAsync("activities", request);

        response.IsSuccessStatusCode.Should().BeTrue(
            "one standard key result satisfies the non-needs-assessment minimum: {0}", await response.Content.ReadAsStringAsync());
        var dto = await response.ToResponseModel<ActivityDto>();

        var saved = await Context.Activities.AsNoTracking().FirstAsync(x => x.Id == new ActivityId(dto.Id));
        saved.IsComplete.Should().BeTrue();
        (await Context.ActivityExpectedResults.AsNoTracking().Where(x => x.ActivityId == saved.Id).ToListAsync())
            .Should().ContainSingle(x => x.KeyResultCode == keyResultCode);
    }

    [Fact]
    public async Task ShouldRejectCompletion_WithOneKeyResult_WhenNeedsAssessment()
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, taskId, keyResultCode) = await GetTypeWithStandardResultAsync(RnaTaskName);

        var request = new CreateActivityRequestDto(
            "Regional needs assessment", categoryId, taskId, null, "planned", null, null, false, [new ExpectedResultInputDto(keyResultCode, null)]);
        var response = await Client.PostAsJsonAsync("activities", request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Select the outcomes you will use to measure this activity");
        (await Context.Activities.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ShouldRejectCompletion_WithOneCustomKeyResult_WhenTypeHasNoStandardOptions()
    {
        AuthenticateAsMainOrgLead();
        var taxonomy = await GetTaxonomyAsync();
        var task = taxonomy.Tasks.First(t => t.Name == OtherStrategyTaskName);
        taxonomy.KeyResults.Should().NotContain(k => k.TaskId == task.Id, "the 'Other type of ...' rows have no standard key results");

        var request = new CreateActivityRequestDto(
            null, task.CategoryId, task.Id, "Bespoke leadership work", "planned", null, null, false, [new ExpectedResultInputDto(null, "Key result one")]);
        var response = await Client.PostAsJsonAsync("activities", request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Context.Activities.AsNoTracking().AnyAsync()).Should().BeFalse();
    }
}