using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
using Application.Common.Csv;
using Application.Common.Features;
using Domain.Activities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.QuarterlyReports;

// BLUM-9104: restoring an older version of the Activity delivery form brings back, withdrawn, an answer option a later
// version published. LAs must not be offered it or be able to give it, as on strategic feedback, but an answer given
// with it before the restore keeps its label. The seeded delivery questions are all free text, so each test adds its
// own radio question to activity_key_results: version (a) offers yes/no, version (b) adds "partly", and restoring (a)
// and publishing gives version (c), in which "partly" is withdrawn. The class shares one database, so every test uses
// its own question code and sets the flag on its first line.
public class ActivityDeliveryWithdrawnOptionTests(IntegrationTestWebFactory factory) : QuarterlyReportTestBase(factory)
{
    private const string FormsRoute = "admin/data-collection-forms";
    private const string DeliveryProgressCode = "AKR_DELIVERY_PROGRESS";
    private const string WithdrawnValue = "partly";
    private const string WithdrawnLabel = "Partly";

    private sealed record FormQuestionResult(Guid Id, Guid? SectionId, string Code);

    private sealed record FormModuleResult(Guid Id, List<FormQuestionResult> Questions);

    private sealed record PublishResult(int LatestVersionNumber);

    private sealed record AnswerErrorRow(string QuestionCode, string Message);

    private sealed record AnswersInvalidEnvelope(string Error, List<AnswerErrorRow> AnswerErrors);

    [Fact]
    public async Task ShouldNotOfferAWithdrawnOption_AndRefuseIt()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        const string code = "AKR_DELIVERY_ON_TRACK";
        var (moduleId, versionA) = await PublishVersionsAsync(code);
        await WithdrawTheOptionAsync(moduleId, versionA);

        var collectionId = await CreateOpenCollectionAsync("Collection on the version with the option withdrawn");
        AuthenticateAsMainOrgLead();
        var activity = await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);

        var question = (await GetActivityQuestionsAsync(collectionId, activity.Id)).Questions.Single(q => q.Code == code);
        question.Options.Select(o => o.Value).Should().Equal(["yes", "no"], "a withdrawn option is not offered");

        var refused = await SaveAnswersAsync(collectionId, activity.Id, saveAsDraft: true, (code, WithdrawnValue));
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var envelope = await refused.ToResponseModel<AnswersInvalidEnvelope>();
        envelope.Error.Should().Be("SUBMISSION_ANSWERS_INVALID");
        envelope.AnswerErrors.Should().ContainSingle().Which.QuestionCode.Should().Be(code);

        var accepted = await SaveAnswersAsync(collectionId, activity.Id, saveAsDraft: true, (code, "yes"));
        accepted.IsSuccessStatusCode.Should().BeTrue(await accepted.Content.ReadAsStringAsync());
        (await AnswersAsync(collectionId, activity.Id)).Should().Contain((code, "yes"));
    }

    [Fact]
    public async Task ShouldNotCarryAWithdrawnOptionForward_IntoTheNextReport()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        const string code = "AKR_DELIVERY_ON_TRACK_CARRIED";
        var (q1, q2, activityId) = await AnsweredWithTheOptionThenWithdrawnAsync(code);

        AuthenticateAsMainOrgLead();
        await CreateSubmissionAsync(q2);
        await ConfirmActivitiesAsync(q2);

        var carried = await AnswersAsync(q2, activityId);
        carried.Should().Contain((DeliveryProgressCode, "Q1 progress."), "the finished activity's other answers carry forward");
        carried.Should().NotContain(answer => answer.Code == code, "an LA can no longer pick the option, so it is not carried");
        (await AnswersAsync(q1, activityId)).Should().Contain((code, WithdrawnValue), "the submitted report keeps its answer");
    }

    [Fact]
    public async Task ShouldStillLabelAnAnswerGivenWithAWithdrawnOption_InTheActivityRecordExport()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        const string code = "AKR_DELIVERY_ON_TRACK_EXPORTED";
        var (_, _, activityId) = await AnsweredWithTheOptionThenWithdrawnAsync(code);
        AuthenticateAsMainOrgLead();

        var response = await Client.GetAsync($"activities/{activityId}/export");

        response.IsSuccessStatusCode.Should().BeTrue("exporting the activity record failed: {0}", await response.Content.ReadAsStringAsync());
        var rows = (await response.Content.ReadAsStringAsync()).Split("\r\n").Select(CsvParser.ParseCsvLine).Skip(1).ToList();
        rows.Should().Contain(
            cells => cells[3] == "Activity delivery" && cells[4] == LabelFor(code) && cells[5] == WithdrawnLabel,
            "the export reads the live form, where the option is withdrawn, and still shows what the answer meant");
    }

    // Q1 opens on version (b), where the option is offered: a finished RNA activity answers it and the report is
    // submitted. The option is then withdrawn (version (c)) and Q2 opens on that version. Returns signed in as admin.
    private async Task<(Guid Q1, Guid Q2, Guid ActivityId)> AnsweredWithTheOptionThenWithdrawnAsync(string code)
    {
        var (moduleId, versionA) = await PublishVersionsAsync(code);
        var q1 = await CreateOpenQuarterCollectionAsync(1, "Q1 offering the option");

        AuthenticateAsMainOrgLead();
        var activity = await CreateRnaActivityAsync(status: "finished");
        await CreateSubmissionAsync(q1);
        await ConfirmActivitiesAsync(q1);
        (await SaveAnswersAsync(q1, activity.Id, saveAsDraft: true, (code, WithdrawnValue))).EnsureSuccessStatusCode();
        await CompleteFinishedRnaReportAsync(q1, activity.Id, "https://example.org/rna", "Q1 progress.");
        await SubmitStartedReportAsync(q1);

        await WithdrawTheOptionAsync(moduleId, versionA);
        var q2 = await CreateOpenQuarterCollectionAsync(2, "Q2 with the option withdrawn");

        return (q1, q2, activity.Id);
    }

    // Publishes the live form (an earlier test's version can be the latest), then version (a) with an optional yes/no
    // radio question in the delivery section, then version (b) adding "partly". Returns the module and version (a)'s
    // number. Runs as admin; identity is AsyncLocal, so the caller signs in again afterwards.
    private async Task<(Guid ModuleId, int VersionA)> PublishVersionsAsync(string code)
    {
        AuthenticateAsAdmin();
        var moduleId = (await Context.FormModules.AsNoTracking().SingleAsync(m => m.Code == KeyResultsModuleCode)).Id.Value;
        await StartDraftAsync(moduleId);
        await PublishAsync(moduleId);

        await StartDraftAsync(moduleId);
        var sectionId = (await GetModuleAsync(moduleId)).Questions.Single(q => q.Code == DeliveryProgressCode).SectionId;
        var create = await Client.PostAsJsonAsync(
            $"{FormsRoute}/modules/{moduleId}/questions",
            new CreateFormQuestionRequestDto(sectionId, code, LabelFor(code), "Radio", false, null, null, [new("yes", "Yes"), new("no", "No")]));
        create.IsSuccessStatusCode.Should().BeTrue(await create.Content.ReadAsStringAsync());
        var versionA = await PublishAsync(moduleId);

        await StartDraftAsync(moduleId);
        var questionId = (await GetModuleAsync(moduleId)).Questions.Single(q => q.Code == code).Id;
        var update = await Client.PutAsJsonAsync(
            $"{FormsRoute}/modules/{moduleId}/questions/{questionId}",
            new UpdateFormQuestionRequestDto(
                sectionId, LabelFor(code), "Radio", false, null, null, [new("yes", "Yes"), new(WithdrawnValue, WithdrawnLabel), new("no", "No")]));
        update.IsSuccessStatusCode.Should().BeTrue(await update.Content.ReadAsStringAsync());
        (await PublishAsync(moduleId)).Should().Be(versionA + 1);

        return (moduleId, versionA);
    }

    // Restores version (a) and publishes it as version (c): "partly" comes back withdrawn, because (b) published it.
    private async Task WithdrawTheOptionAsync(Guid moduleId, int versionA)
    {
        AuthenticateAsAdmin();
        var restore = await Client.PostAsync($"{FormsRoute}/modules/{moduleId}/versions/{versionA}/restore", null);
        restore.StatusCode.Should().Be(HttpStatusCode.OK, await restore.Content.ReadAsStringAsync());
        (await PublishAsync(moduleId)).Should().Be(versionA + 2);
    }

    private async Task StartDraftAsync(Guid moduleId)
    {
        var draft = await Client.PostAsync($"{FormsRoute}/modules/{moduleId}/draft", null);
        draft.StatusCode.Should().Be(HttpStatusCode.OK, await draft.Content.ReadAsStringAsync());
    }

    private async Task<int> PublishAsync(Guid moduleId)
    {
        var publish = await Client.PostAsync($"{FormsRoute}/modules/{moduleId}/publish", null);
        publish.StatusCode.Should().Be(HttpStatusCode.OK, await publish.Content.ReadAsStringAsync());
        return (await publish.ToResponseModel<PublishResult>()).LatestVersionNumber;
    }

    private async Task<FormModuleResult> GetModuleAsync(Guid moduleId)
    {
        var response = await Client.GetAsync($"{FormsRoute}/modules/{moduleId}");
        response.IsSuccessStatusCode.Should().BeTrue();
        return await response.ToResponseModel<FormModuleResult>();
    }

    private async Task<List<(string Code, string Value)>> AnswersAsync(Guid collectionId, Guid activityId)
    {
        var submissionId = await GetSubmissionIdAsync(collectionId);
        var rows = await Context.SubmissionActivityAnswers
            .AsNoTracking()
            .Where(a => a.SubmissionId == submissionId && a.ActivityId == new ActivityId(activityId))
            .ToListAsync();

        return rows.Select(a => (a.QuestionCode, a.Value)).ToList();
    }

    private static string LabelFor(string code) => $"Is the activity on track? ({code})";
}