using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
using Domain.Activities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.Activities;

// Minimum key-result selections on completion. The 13 August Question Protocol (BLUM-7620) struck needs
// assessment's "<2 selected" trigger through in favour of "<1 selected" and reworded every screen's error and
// hint to "at least one", so ONE measure now completes any activity — including the free-text-only "Other type
// of ..." screens, which used to need 2. Drafts are never checked.
//
// The message literals asserted here are also pinned deliberately - see the last two tests.
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

    // Needs assessment used to be the one type that demanded two. The 13 August protocol struck that trigger
    // through, so one standard measure now completes it like every other type.
    [Fact]
    public async Task ShouldCompleteActivity_WithOneStandardKeyResult_WhenNeedsAssessment()
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, taskId, keyResultCode) = await GetTypeWithStandardResultAsync(RnaTaskName);

        var request = new CreateActivityRequestDto(
            "Regional needs assessment", categoryId, taskId, null, "planned", null, null, false, [new ExpectedResultInputDto(keyResultCode, null)]);
        var response = await Client.PostAsJsonAsync("activities", request);

        response.IsSuccessStatusCode.Should().BeTrue(
            "the needs-assessment minimum dropped from 2 to 1: {0}", await response.Content.ReadAsStringAsync());
        var dto = await response.ToResponseModel<ActivityDto>();
        (await Context.Activities.AsNoTracking().FirstAsync(x => x.Id == new ActivityId(dto.Id))).IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldCompleteActivity_WithOneCustomKeyResult_WhenTypeHasNoStandardOptions()
    {
        AuthenticateAsMainOrgLead();
        var taxonomy = await GetTaxonomyAsync();
        var task = taxonomy.Tasks.First(t => t.Name == OtherStrategyTaskName);
        taxonomy.KeyResults.Should().NotContain(k => k.TaskId == task.Id, "the 'Other type of ...' rows have no standard key results");

        var request = new CreateActivityRequestDto(
            "Bespoke leadership work", task.CategoryId, task.Id, "Bespoke leadership work", "planned", null, null, false, [new ExpectedResultInputDto(null, "Key result one")]);
        var response = await Client.PostAsJsonAsync("activities", request);

        response.IsSuccessStatusCode.Should().BeTrue(
            "the free-text screens dropped from 2 entries to 1: {0}", await response.Content.ReadAsStringAsync());
        (await Context.Activities.AsNoTracking().AnyAsync()).Should().BeTrue();
    }

    // The two minimum messages differ by screen: a checkbox list of standard measures says "Select at least one
    // measure", the free-text-only "Other type of ..." screens say "Enter at least one measure".
    [Fact]
    public async Task ShouldRejectCompletion_WithNoKeyResults_UsingTheSelectMessage()
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, taskId, _) = await GetTypeWithStandardResultAsync(SteeringGroupTaskName);

        var request = new CreateActivityRequestDto(
            "Steering group", categoryId, taskId, null, "planned", null, null, false, []);
        var response = await Client.PostAsJsonAsync("activities", request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Select at least one measure");
        (await Context.Activities.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ShouldRejectCompletion_WithNoKeyResults_UsingTheEnterMessage_WhenTypeHasNoStandardOptions()
    {
        AuthenticateAsMainOrgLead();
        var taxonomy = await GetTaxonomyAsync();
        var task = taxonomy.Tasks.First(t => t.Name == OtherStrategyTaskName);

        var request = new CreateActivityRequestDto(
            "Bespoke leadership work", task.CategoryId, task.Id, "Bespoke leadership work", "planned", null, null, false, []);
        var response = await Client.PostAsJsonAsync("activities", request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Enter at least one measure");
        (await Context.Activities.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    // All three expected-result refusals are a cross-stack copy contract: the client matches these messages
    // verbatim (EXPECTED_RESULTS_MESSAGES in client/src/views/Activities/messages.ts) to decide the error belongs
    // on the expected-results field rather than the generic error summary. Reword one side without the other and
    // the error silently moves to the page-level banner - no inline error, and an error-summary link that no
    // longer reaches the field. Nothing throws and no other test fails, so these assertions are the whole guard
    // (the two minimum literals are pinned by the pair of tests above).
    // BLUM-7563 replaced a tolerant /key result/i match on the client with exact equality, which is what makes
    // an unnoticed reword possible.
    [Fact]
    public async Task ShouldRejectAnUnknownKeyResultCode_WithTheMessageTheClientMatchesOn()
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, taskId, _) = await GetTypeWithStandardResultAsync(SteeringGroupTaskName);

        var request = new CreateActivityRequestDto(
            "Steering group", categoryId, taskId, null, "planned", null, null, false, [new ExpectedResultInputDto("not_a_seeded_code", null)]);
        var response = await Client.PostAsJsonAsync("activities", request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync())
            .Should().Contain("One or more selected outcomes are not valid for this activity type.");
        (await Context.Activities.AsNoTracking().AnyAsync()).Should().BeFalse();
    }
}