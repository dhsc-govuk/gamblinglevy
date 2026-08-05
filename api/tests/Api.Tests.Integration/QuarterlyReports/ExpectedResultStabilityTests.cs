using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
using Domain.Activities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.QuarterlyReports;

// BLUM-6805: expected-result row ids are embedded in the quarterly report's custom question codes
// (CUSTOM_{id:N}), so activity edits must diff/merge rows instead of recreating them — otherwise saved
// report answers are orphaned and completing the activity report 422s.
public class ExpectedResultStabilityTests(IntegrationTestWebFactory factory) : QuarterlyReportTestBase(factory)
{
    private const string CustomPrefix = "CUSTOM_";

    // Resends the edit-screen payload for a completed activity: same category/type (ignored once complete),
    // active status and the given expected results. Resends the activity's own saved answers, exactly as the edit
    // screen does — the update replaces the answers to every applicable question, so anything not resent is
    // deleted, and the mandatory "Activity details" questions (BLUM-7041) would 422.
    private async Task UpdateActivityAsync(ActivityDto activity, string title, IReadOnlyList<ExpectedResultInputDto> expectedResults)
    {
        var answers = activity.Answers.Select(a => new ActivityAnswerInputDto(a.QuestionCode, a.Value)).ToList();
        var response = await Client.PutAsJsonAsync(
            $"activities/{activity.Id}",
            new UpdateActivityRequestDto(title, activity.CategoryId, activity.TaskId, null, "active", 4, 2026, false, expectedResults, answers));
        response.IsSuccessStatusCode.Should().BeTrue(
            "updating activity {0} failed: {1}", activity.Id, await response.Content.ReadAsStringAsync());
    }

    private async Task<(Guid CollectionId, ActivityDto Activity)> StartConfirmedReportAsync(
        IReadOnlyList<ExpectedResultInputDto>? expectedResults = null)
    {
        var collectionId = await CreateOpenCollectionAsync();
        var activity = await CreateCompletedActivityAsync(expectedResults: expectedResults);
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        return (collectionId, activity);
    }

    [Fact]
    public async Task ShouldKeepExpectedResultRows_WhenOnlyTheTitleChanges()
    {
        AuthenticateAsMainOrgLead();
        var results = new List<ExpectedResultInputDto>
        {
            new("rna_contributors", null),
            new(null, "Key result one"),
            new(null, "Key result two"),
        };
        var activity = await CreateCompletedActivityAsync(expectedResults: results);
        var rowsBefore = await GetExpectedResultRowsAsync(activity.Id);
        rowsBefore.Should().HaveCount(3);

        await UpdateActivityAsync(activity, "Updated title only", results);

        var rowsAfter = await GetExpectedResultRowsAsync(activity.Id);
        rowsAfter.Select(x => new { x.Id, x.CreatedAt, x.KeyResultCode, x.Text, x.DisplayOrder })
            .Should().BeEquivalentTo(rowsBefore.Select(x => new { x.Id, x.CreatedAt, x.KeyResultCode, x.Text, x.DisplayOrder }));
    }

    [Fact]
    public async Task ShouldKeepCustomAnswers_AcrossATitleEdit_AndStillComplete()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, activity) = await StartConfirmedReportAsync();
        await CompleteActivityReportAsync(collectionId, activity.Id);

        var before = await GetActivityQuestionsAsync(collectionId, activity.Id);
        var customCodes = before.Questions.Where(q => q.Code.StartsWith(CustomPrefix)).Select(q => q.Code).ToList();
        customCodes.Should().HaveCount(2, "the default expected results are two custom entries");

        await UpdateActivityAsync(activity, "Renamed after reporting", DefaultExpectedResults);

        var after = await GetActivityQuestionsAsync(collectionId, activity.Id);
        after.Questions.Where(q => q.Code.StartsWith(CustomPrefix)).Select(q => q.Code)
            .Should().BeEquivalentTo(customCodes, "custom question codes must survive an activity edit");
        foreach (var code in customCodes)
        {
            after.Answers.Should().Contain(a => a.QuestionCode == code && !string.IsNullOrEmpty(a.Value));
        }

        // The review flow re-saves what the form renders and completes again — this must not 422.
        var resend = after.Answers
            .Where(a => !string.IsNullOrEmpty(a.Value))
            .Select(a => (a.QuestionCode, a.Value))
            .ToArray();
        (await SaveAnswersAsync(collectionId, activity.Id, saveAsDraft: true, resend)).EnsureSuccessStatusCode();
        var complete = await CompleteActivityAsync(collectionId, activity.Id);
        complete.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "re-completing after an activity edit failed: {0}",
            await complete.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ShouldKeepTheAnswer_WhenACustomResultIsReworded()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, activity) = await StartConfirmedReportAsync();

        var before = await GetActivityQuestionsAsync(collectionId, activity.Id);
        var target = before.Questions.Single(q => q.Label == "Provide progress details for: Key result one");
        (await SaveAnswersAsync(collectionId, activity.Id, saveAsDraft: true, (target.Code, "Progress on result one")))
            .EnsureSuccessStatusCode();

        await UpdateActivityAsync(activity, "Same title", [new(null, "Key result one (reworded)"), new(null, "Key result two")]);

        var after = await GetActivityQuestionsAsync(collectionId, activity.Id);
        var reworded = after.Questions.Single(q => q.Code == target.Code);
        reworded.Label.Should().Be("Provide progress details for: Key result one (reworded)");
        after.Answers.Should().Contain(a => a.QuestionCode == target.Code && a.Value == "Progress on result one");
    }

    [Fact]
    public async Task ShouldDropTheQuestion_WhenACustomResultIsRemoved()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, activity) = await StartConfirmedReportAsync();

        var before = await GetActivityQuestionsAsync(collectionId, activity.Id);
        var keep = before.Questions.Single(q => q.Label == "Provide progress details for: Key result one");
        var drop = before.Questions.Single(q => q.Label == "Provide progress details for: Key result two");
        (await SaveAnswersAsync(
            collectionId, activity.Id, saveAsDraft: true, (keep.Code, "First answer"), (drop.Code, "Second answer")))
            .EnsureSuccessStatusCode();

        // "Key result two" is swapped for a standard selection so the "select at least 2" rule still holds.
        await UpdateActivityAsync(activity, "Same title", [new(null, "Key result one"), new("rna_contributors", null)]);

        var after = await GetActivityQuestionsAsync(collectionId, activity.Id);
        after.Questions.Select(q => q.Code).Should().Contain(keep.Code).And.NotContain(drop.Code);
        after.Answers.Should().Contain(a => a.QuestionCode == keep.Code && a.Value == "First answer");

        // Completing succeeds without the removed question, and the complete step prunes its orphaned answer.
        await CompleteActivityReportAsync(collectionId, activity.Id);
        (await Context.SubmissionActivityAnswers.AsNoTracking().AnyAsync(a => a.QuestionCode == drop.Code))
            .Should().BeFalse();
    }

    private async Task<List<ActivityExpectedResult>> GetExpectedResultRowsAsync(Guid activityId) =>
        await Context.ActivityExpectedResults.AsNoTracking()
            .Where(x => x.ActivityId == new ActivityId(activityId))
            .OrderBy(x => x.DisplayOrder)
            .ToListAsync();
}