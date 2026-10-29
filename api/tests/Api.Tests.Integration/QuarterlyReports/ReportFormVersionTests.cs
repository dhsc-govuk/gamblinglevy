using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
using Application.Common.Features;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.QuarterlyReports;

// BLUM-9104: with BLUM_9087 on, a quarterly report reads the form questions of its collection's recorded version, so two
// collections open at once each see their own. Collection Q1 records version 1 of both form modules; DHSC then publishes
// version 2 (reworded questions, one feedback question made optional); collection Q2 records version 2. Every test sets
// the flag on its first line: the settings singleton outlives a test.
public class ReportFormVersionTests(IntegrationTestWebFactory factory) : QuarterlyReportTestBase(factory)
{
    private const string FormsRoute = "admin/data-collection-forms";
    private const string OtherPreventionCode = "SF_OTHER_PREVENTION";
    private const string DeliveryProgressCode = "AKR_DELIVERY_PROGRESS";
    private const string Reworded = " (version 2)";

    private sealed record FormQuestionResult(Guid Id, Guid? SectionId, string Code, string Label, string QuestionType, bool IsRequired, string? Hint, string? HelpText);

    private sealed record FormModuleResult(Guid Id, List<FormQuestionResult> Questions);

    private sealed record ErrorEnvelope(string Error);

    [Fact]
    public async Task ShouldShowEachCollectionItsOwnVersionOfTheFeedbackQuestions()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var (q1, q2, _) = await TwoCollectionsOnDifferentVersionsAsync();
        AuthenticateAsMainOrgLead();

        var feedback1 = await GetStrategicFeedbackAsync(q1);
        var feedback2 = await GetStrategicFeedbackAsync(q2);

        var otherPrevention1 = feedback1.Pages.SelectMany(p => p.Questions).Single(q => q.Code == OtherPreventionCode);
        var otherPrevention2 = feedback2.Pages.SelectMany(p => p.Questions).Single(q => q.Code == OtherPreventionCode);
        otherPrevention2.Label.Should().Be(otherPrevention1.Label + Reworded);
        otherPrevention1.IsRequired.Should().BeTrue();
        otherPrevention2.IsRequired.Should().BeFalse();
        otherPrevention2.Id.Should().Be(otherPrevention1.Id, "a version keeps the question's id");
    }

    [Fact]
    public async Task ShouldCheckEachReportAgainstItsOwnVersion_WhenFeedbackIsCompleted()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var (q1, q2, _) = await TwoCollectionsOnDifferentVersionsAsync();
        AuthenticateAsMainOrgLead();

        await SaveEveryFeedbackAnswerExceptOtherPreventionAsync(q1);
        await SaveEveryFeedbackAnswerExceptOtherPreventionAsync(q2);
        var completeQ1 = await CompleteStrategicFeedbackAsync(q1);
        var completeQ2 = await CompleteStrategicFeedbackAsync(q2);

        completeQ1.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "version 1 still requires the question");
        (await completeQ1.ToResponseModel<ErrorEnvelope>()).Error.Should().Be("SUBMISSION_ANSWERS_INVALID");
        completeQ2.IsSuccessStatusCode.Should().BeTrue("version 2 made it optional: {0}", await completeQ2.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ShouldShowEachCollectionItsOwnVersionOfTheDeliveryQuestions()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var (q1, q2, activityId) = await TwoCollectionsOnDifferentVersionsAsync();
        AuthenticateAsMainOrgLead();

        var questions1 = await GetActivityQuestionsAsync(q1, activityId);
        var questions2 = await GetActivityQuestionsAsync(q2, activityId);

        var label1 = questions1.Questions.Single(q => q.Code == DeliveryProgressCode).Label;
        questions2.Questions.Single(q => q.Code == DeliveryProgressCode).Label.Should().Be(label1 + Reworded);
    }

    [Fact]
    public async Task ShouldShowTheLiveQuestionsInEveryCollection_WhenTheFlagIsOff()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var (q1, q2, activityId) = await TwoCollectionsOnDifferentVersionsAsync();
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: false);
        AuthenticateAsMainOrgLead();

        foreach (var collectionId in new[] { q1, q2 })
        {
            var feedback = await GetStrategicFeedbackAsync(collectionId);
            var live = await LiveQuestionAsync("strategic_feedback", OtherPreventionCode);
            feedback.Pages.SelectMany(p => p.Questions).Single(q => q.Code == OtherPreventionCode).Label.Should().Be(live.Label);

            var delivery = (await GetActivityQuestionsAsync(collectionId, activityId)).Questions.Single(q => q.Code == DeliveryProgressCode);
            delivery.Label.Should().Be((await LiveQuestionAsync("activity_key_results", DeliveryProgressCode)).Label);
        }
    }

    // Q1 is published (recording version 1 of every form), then both form modules get a version 2, then Q2 is published
    // (recording version 2). The Main-org lead has an activity and a started, confirmed report in each. Identity is
    // AsyncLocal, so the caller signs in as that lead again after this returns.
    private async Task<(Guid Q1, Guid Q2, Guid ActivityId)> TwoCollectionsOnDifferentVersionsAsync()
    {
        // The class shares one database, so an earlier test's version can be the latest. The strategic-feedback seeder
        // resets the live questions to its wording only while the flag is off (BLUM-9105), so switch it off and on again;
        // publishing the live forms then makes the latest version match them.
        AuthenticateAsAdmin();
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: false);
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        await PublishLiveAsync("strategic_feedback");
        await PublishLiveAsync("activity_key_results");

        var q1 = await CreateOpenQuarterCollectionAsync(1, "Q1 on version 1");

        AuthenticateAsAdmin();
        await PublishVersionTwoAsync("strategic_feedback", OtherPreventionCode, isRequired: false);
        await PublishVersionTwoAsync("activity_key_results", DeliveryProgressCode, isRequired: null);

        var q2 = await CreateOpenQuarterCollectionAsync(2, "Q2 on version 2");

        AuthenticateAsMainOrgLead();
        var activity = await CreateRnaActivityAsync();
        foreach (var collectionId in new[] { q1, q2 })
        {
            await CreateSubmissionAsync(collectionId);
            await ConfirmActivitiesAsync(collectionId);
        }

        return (q1, q2, activity.Id);
    }

    // Reopens the module and publishes it unchanged: a new version only if the live form differs from the latest one.
    private async Task PublishLiveAsync(string moduleCode)
    {
        var module = await Context.FormModules.AsNoTracking().SingleAsync(m => m.Code == moduleCode);
        (await Client.PostAsync($"{FormsRoute}/modules/{module.Id.Value}/draft", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client.PostAsync($"{FormsRoute}/modules/{module.Id.Value}/publish", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Reopens the module, rewords one question (and optionally changes whether it is required) and publishes it.
    private async Task PublishVersionTwoAsync(string moduleCode, string questionCode, bool? isRequired)
    {
        var module = await Context.FormModules.AsNoTracking().SingleAsync(m => m.Code == moduleCode);
        (await Client.PostAsync($"{FormsRoute}/modules/{module.Id.Value}/draft", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var question = (await (await Client.GetAsync($"{FormsRoute}/modules/{module.Id.Value}")).ToResponseModel<FormModuleResult>())
            .Questions.Single(q => q.Code == questionCode);
        var update = await Client.PutAsJsonAsync(
            $"{FormsRoute}/modules/{module.Id.Value}/questions/{question.Id}",
            new UpdateFormQuestionRequestDto(question.SectionId, question.Label + Reworded, question.QuestionType, isRequired ?? question.IsRequired, question.Hint, question.HelpText, null));
        update.IsSuccessStatusCode.Should().BeTrue(await update.Content.ReadAsStringAsync());

        var publish = await Client.PostAsync($"{FormsRoute}/modules/{module.Id.Value}/publish", null);
        publish.StatusCode.Should().Be(HttpStatusCode.OK, await publish.Content.ReadAsStringAsync());
    }

    private async Task SaveEveryFeedbackAnswerExceptOtherPreventionAsync(Guid collectionId)
    {
        var feedback = await GetStrategicFeedbackAsync(collectionId);
        var answers = feedback.Pages
            .SelectMany(p => p.Questions)
            .Where(q => q.Code != OtherPreventionCode)
            .Select(q => (q.Code, (string?)(q.Options.FirstOrDefault()?.Value ?? "Feedback answer for this quarter.")))
            .ToArray();
        (await SaveStrategicFeedbackAsync(collectionId, saveAsDraft: true, answers)).EnsureSuccessStatusCode();
    }

    private Task<Domain.DataCollectionForms.DcFormQuestion> LiveQuestionAsync(string moduleCode, string questionCode) =>
        Context.DcFormQuestions.AsNoTracking()
            .Where(q => q.Code == questionCode)
            .Join(Context.FormModules.Where(m => m.Code == moduleCode), q => q.FormModuleId, m => m.Id, (q, _) => q)
            .SingleAsync();
}