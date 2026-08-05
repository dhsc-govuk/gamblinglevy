using Api.Dtos;
using FluentAssertions;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.QuarterlyReports;

// Review flags (BLUM-6611): once the activities list has been confirmed, any create / soft-delete / restore of a
// completed activity flags the submission and every started section for review; reconfirming the list — or
// re-completing a flagged section — clears the flags.
public class ReviewRequiredTests(IntegrationTestWebFactory factory) : QuarterlyReportTestBase(factory)
{
    // Confirmed report with one in-progress activity, so the key-results section is "started" and flaggable.
    // The caller must already be authenticated as a Main-org activity manager: identity is AsyncLocal-scoped, so
    // a helper cannot switch it for its caller — every test sets it in its own body first.
    private async Task<(Guid CollectionId, ActivityDto Activity)> StartFlaggableReportAsync()
    {
        var collectionId = await CreateOpenCollectionAsync();
        var activity = await CreateRnaActivityAsync(title: "Original activity");
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        (await SaveAnswersAsync(collectionId, activity.Id, saveAsDraft: true, ("AKR_DELIVERY_PROGRESS", "WIP")))
            .EnsureSuccessStatusCode();
        return (collectionId, activity);
    }

    private static void AssertFlagged(SubmissionTaskListDto taskList)
    {
        taskList.ActivitiesReviewRequired.Should().BeTrue();
        taskList.Sections.Single(s => s.ModuleCode == KeyResultsModuleCode).ReviewRequired.Should().BeTrue();
    }

    private static void AssertNotFlagged(SubmissionTaskListDto taskList)
    {
        taskList.ActivitiesReviewRequired.Should().BeFalse();
        taskList.Sections.Should().OnlyContain(s => !s.ReviewRequired);
    }

    [Fact]
    public async Task CreatingACompletedActivityAfterConfirmation_FlagsOnlyStartedSections()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, _) = await StartFlaggableReportAsync();

        await CreateRnaActivityAsync(title: "Added after confirmation");

        var taskList = await GetTaskListAsync(collectionId);
        AssertFlagged(taskList);

        // Sections the user has not started stay unflagged.
        taskList.Sections.Single(s => s.ModuleCode == FundingModuleCode).ReviewRequired.Should().BeFalse();
        taskList.Sections.Single(s => s.ModuleCode == FeedbackModuleCode).ReviewRequired.Should().BeFalse();
    }

    [Fact]
    public async Task Reconfirming_ClearsTheReviewFlags()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, _) = await StartFlaggableReportAsync();
        await CreateRnaActivityAsync(title: "Added after confirmation");
        AssertFlagged(await GetTaskListAsync(collectionId));

        await ConfirmActivitiesAsync(collectionId);

        AssertNotFlagged(await GetTaskListAsync(collectionId));
    }

    [Fact]
    public async Task SoftDeletingAndRestoringACompletedActivity_EachFlagReview()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, _) = await StartFlaggableReportAsync();
        var second = await CreateRnaActivityAsync(title: "To delete and restore");
        await ConfirmActivitiesAsync(collectionId);
        AssertNotFlagged(await GetTaskListAsync(collectionId));

        (await Client.DeleteAsync($"activities/{second.Id}")).EnsureSuccessStatusCode();

        AssertFlagged(await GetTaskListAsync(collectionId));

        await ConfirmActivitiesAsync(collectionId);
        AssertNotFlagged(await GetTaskListAsync(collectionId));

        (await Client.PostAsync($"activities/{second.Id}/restore", null)).EnsureSuccessStatusCode();

        AssertFlagged(await GetTaskListAsync(collectionId));
    }

    [Fact]
    public async Task ActivityChangesInAnotherOrganisation_DoNotFlagTheSubmission()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, _) = await StartFlaggableReportAsync();

        AuthenticateAsSecondOrgLead();
        await CreateRnaActivityAsync(title: "Second org activity");

        AuthenticateAsMainOrgLead();
        AssertNotFlagged(await GetTaskListAsync(collectionId));
    }

    [Fact]
    public async Task RecompletingTheFlaggedSection_ClearsItsFlagAndTheSubmissionFlag()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, first) = await StartFlaggableReportAsync();
        var second = await CreateRnaActivityAsync(title: "Added after confirmation");
        AssertFlagged(await GetTaskListAsync(collectionId));

        // Completing every reportable activity completes the section, which counts as reviewing it.
        await CompleteActivityReportAsync(collectionId, first.Id);
        await CompleteActivityReportAsync(collectionId, second.Id);

        var taskList = await GetTaskListAsync(collectionId);
        taskList.Sections.Single(s => s.ModuleCode == KeyResultsModuleCode).Status.Should().Be("completed");
        AssertNotFlagged(taskList);
    }
}