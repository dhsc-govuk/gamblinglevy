using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
using Domain.Activities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.QuarterlyReports;

// GET quarterly-report/{collectionId}/activities/{activityId}/questions — protocol question resolution against the
// startup-seeded key-result protocol and the published activity_key_results module.
public class QuestionResolutionTests(IntegrationTestWebFactory factory) : QuarterlyReportTestBase(factory)
{
    private static readonly string[] DeliveryCodes =
    [
        "AKR_DELIVERY_PROGRESS",
        "AKR_DELIVERY_CHALLENGES",
        "AKR_DELIVERY_NEXT_QUARTER",
    ];

    private sealed record AnswerErrorRow(string QuestionCode, string Message);

    private sealed record AnswersInvalidEnvelope(string Error, string Message, List<AnswerErrorRow> AnswerErrors);

    // A collection with a started report. The caller must already be authenticated as a Main-org activity
    // manager: identity is AsyncLocal-scoped, so a helper cannot switch it for its caller — every test sets it
    // in its own body first (the repo-wide convention).
    private async Task<Guid> StartReportAsync()
    {
        var collectionId = await CreateOpenCollectionAsync();
        await CreateSubmissionAsync(collectionId);
        return collectionId;
    }

    [Fact]
    public async Task ActiveRnaActivity_GetsActivePhrasingForSelectedKeyResultsPlusDeliveryQuestions()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartReportAsync();
        var activity = await CreateRnaActivityAsync();

        var dto = await GetActivityQuestionsAsync(collectionId, activity.Id);

        dto.ActivityStatus.Should().Be("active");
        dto.ReportStatus.Should().Be("not_started");

        // rna_contributors unlocks its Active variants; rna_published has no Active question at all.
        var resultsQuestions = dto.Questions.Where(q => q.Section == "results").ToList();
        resultsQuestions.Select(q => q.Code).Should().Equal("RNA_CONTRIBUTORS", "RNA_CONTRIBUTORS_OTHER");
        resultsQuestions[0].Label.Should().Be("Who is contributing evidence?");
        resultsQuestions[0].QuestionType.Should().Be("Checkbox");
        resultsQuestions[1].ConditionalQuestionCode.Should().Be("RNA_CONTRIBUTORS");
        resultsQuestions[1].ConditionalValue.Should().Be("other");

        // Unselected key results (rna_information) contribute nothing.
        dto.Questions.Should().NotContain(q => q.Code == "RNA_INFORMATION");

        var delivery = dto.Questions.Where(q => q.Section == "delivery").ToList();
        delivery.Select(q => q.Code).Should().Equal(DeliveryCodes);
        delivery.Single(q => q.Code == "AKR_DELIVERY_PROGRESS").IsRequired.Should().BeTrue();
        delivery.Where(q => q.Code != "AKR_DELIVERY_PROGRESS").Should().OnlyContain(q => !q.IsRequired);
    }

    [Fact]
    public async Task FinishedRnaActivity_GetsClosedPhrasingAndFinishedOnlyConditionalChildren()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartReportAsync();
        var activity = await CreateRnaActivityAsync(status: "finished");

        var dto = await GetActivityQuestionsAsync(collectionId, activity.Id);

        var resultsQuestions = dto.Questions.Where(q => q.Section == "results").ToList();
        resultsQuestions.Select(q => q.Code).Should().Equal(
            "RNA_CONTRIBUTORS", "RNA_CONTRIBUTORS_OTHER", "RNA_PUBLISHED", "RNA_PUBLISHED_URL");
        resultsQuestions[0].Label.Should().Be("Who contributed evidence?");

        // Boolean protocol questions arrive as Radio with synthesised yes/no options.
        var published = resultsQuestions.Single(q => q.Code == "RNA_PUBLISHED");
        published.QuestionType.Should().Be("Radio");
        published.Options.Select(o => o.Value).Should().Equal("yes", "no");

        var publishedUrl = resultsQuestions.Single(q => q.Code == "RNA_PUBLISHED_URL");
        publishedUrl.ConditionalQuestionCode.Should().Be("RNA_PUBLISHED");
        publishedUrl.ConditionalValue.Should().Be("yes");
    }

    [Fact]
    public async Task ClosedRnaActivity_DoesNotGetTheFinishedOnlyChildren()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartReportAsync();
        var activity = await CreateRnaActivityAsync(status: "closed");

        var dto = await GetActivityQuestionsAsync(collectionId, activity.Id);

        var codes = dto.Questions.Where(q => q.Section == "results").Select(q => q.Code).ToList();
        codes.Should().Contain("RNA_PUBLISHED");
        codes.Should().NotContain("RNA_PUBLISHED_URL");
    }

    // BLUM-7472: the information list offers an "Other" option, so it must resolve alongside a conditional
    // free-text companion — without one the LA can tick "Other" and has nowhere to say what it was, and the
    // checkbox validator refuses free text posted into the parent code.
    [Fact]
    public async Task RnaInformationKeyResult_GetsAnOtherFreeTextCompanionAfterItsParent()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartReportAsync();
        var (_, taskId) = await GetTypeAsync(RnaTaskName);

        // Needs assessment keeps the protocol's "select at least 2" rule, so rna_published rides along.
        var results = await StandardResultsAsync(taskId, "rna_information", "rna_published");
        var activity = await CreateCompletedActivityAsync(RnaTaskName, "closed", results);

        var dto = await GetActivityQuestionsAsync(collectionId, activity.Id);

        var resultsQuestions = dto.Questions.Where(q => q.Section == "results").ToList();
        resultsQuestions.Select(q => q.Code).Should().Equal("RNA_INFORMATION", "RNA_INFORMATION_OTHER", "RNA_PUBLISHED");

        var parent = resultsQuestions[0];
        parent.Label.Should().Be("What information does the assessment cover?");
        parent.QuestionType.Should().Be("Checkbox");
        parent.Options.Select(o => o.Value).Should().Contain("other");

        var companion = resultsQuestions[1];
        companion.Label.Should().Be("What other information does the assessment cover?");
        companion.QuestionType.Should().Be("Textarea");
        companion.ConditionalQuestionCode.Should().Be("RNA_INFORMATION");
        companion.ConditionalValue.Should().Be("other");
    }

    // BLUM-7475: the protocol sheet marks this question "List/text" but supplies no options, so it shipped as free
    // text and QA saw a textarea where a list of services belongs. It is now a checkbox list over the shared "Who
    // will you work with?" options, with the conditional "Other" companion every such list needs.
    [Fact]
    public async Task ClosedScreeningActivity_GetsTheServicesListPlusItsOtherFreeTextCompanion()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartReportAsync();
        var (_, taskId) = await GetTypeAsync(ScreeningTaskName);

        // Help types carry standard definitions, so one selection satisfies the completion minimum.
        var results = await StandardResultsAsync(taskId, "scr_tool");
        var activity = await CreateCompletedActivityAsync(ScreeningTaskName, "closed", results);

        var dto = await GetActivityQuestionsAsync(collectionId, activity.Id);

        var resultsQuestions = dto.Questions.Where(q => q.Section == "results").ToList();
        resultsQuestions.Select(q => q.Code).Should().Equal("SCR_SERVICES", "SCR_SERVICES_OTHER");

        var parent = resultsQuestions[0];
        parent.Label.Should().Be("Which services are currently screening for gambling harms?");
        parent.QuestionType.Should().Be("Checkbox");
        parent.Options.Select(o => o.Value).Should().Contain(["police", "mental_health", "other"]);

        var companion = resultsQuestions[1];
        companion.Label.Should().Be("Which other services are currently screening for gambling harms?");
        companion.QuestionType.Should().Be("Textarea");
        companion.ConditionalQuestionCode.Should().Be("SCR_SERVICES");
        companion.ConditionalValue.Should().Be("other");
    }

    // The Active variant keeps its own SCR_TOOL yes/no. The two are separate codes on purpose: answers key on Code
    // across a status change, so one code answering to both a radio and a checkbox validator would leave a saved
    // "yes" failing the option allow-list the moment the activity closed.
    [Fact]
    public async Task ActiveScreeningActivity_GetsTheYesNoToolQuestionAndNotTheServicesList()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartReportAsync();
        var (_, taskId) = await GetTypeAsync(ScreeningTaskName);
        var results = await StandardResultsAsync(taskId, "scr_tool");
        var activity = await CreateCompletedActivityAsync(ScreeningTaskName, "active", results);

        var dto = await GetActivityQuestionsAsync(collectionId, activity.Id);

        var resultsQuestions = dto.Questions.Where(q => q.Section == "results").ToList();
        resultsQuestions.Select(q => q.Code).Should().Equal("SCR_TOOL");
        resultsQuestions[0].QuestionType.Should().Be("Radio");
        resultsQuestions[0].Options.Select(o => o.Value).Should().Equal("yes", "no");
    }

    [Fact]
    public async Task CustomKeyResults_GetGenericRequiredProgressTextareas()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartReportAsync();
        var activity = await CreateCompletedActivityAsync(expectedResults: DefaultExpectedResults);

        var dto = await GetActivityQuestionsAsync(collectionId, activity.Id);

        var resultsQuestions = dto.Questions.Where(q => q.Section == "results").ToList();
        resultsQuestions.Should().HaveCount(2);
        resultsQuestions.Should().OnlyContain(q => q.Code.StartsWith("CUSTOM_") && q.QuestionType == "Textarea" && q.IsRequired);
        resultsQuestions[0].Label.Should().Be("What progress have you made towards 'Key result one'?");
        resultsQuestions[1].Label.Should().Be("What progress have you made towards 'Key result two'?");
    }

    [Fact]
    public async Task PeopleAndRolesActivity_GetsOnlyTheDeliveryQuestions()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartReportAsync();
        var (categoryId, taskId) = await GetTypeAsync(PeopleTaskName);

        // People-and-roles records have no key results (allowed: the >= 2 rule does not apply to that category)
        // but must answer the required seeded "Role details" questions to complete setup. Two since BLUM-7871
        // withdrew PR_ROLE_SHARED — an answer to a code the schema no longer returns is silently dropped by
        // CollectPersistable rather than rejected, so leaving it here would have gone on passing while meaning
        // nothing.
        var request = new CreateActivityRequestDto(
            "Public health officer role",
            categoryId,
            taskId,
            null,
            "active",
            4,
            2026,
            false,
            Array.Empty<ExpectedResultInputDto>(),
            new List<ActivityAnswerInputDto>
            {
                new("PR_FUNDING_TIME", "up_to_20"),
                new("PR_ROLE_DURATION", "6_12"),
            });
        var response = await Client.PostAsJsonAsync("activities", request);
        response.IsSuccessStatusCode.Should().BeTrue(
            "creating the People-and-roles activity failed: {0}", await response.Content.ReadAsStringAsync());
        var activity = await response.ToResponseModel<ActivityDto>();

        var dto = await GetActivityQuestionsAsync(collectionId, activity.Id);

        dto.Questions.Should().HaveCount(3);
        dto.Questions.Should().OnlyContain(q => q.Section == "delivery");
        dto.Questions.Select(q => q.Code).Should().Equal(DeliveryCodes);

        // The module's own wording — the Joint partnership copy (below) must not leak into any other category.
        dto.Questions.Single(q => q.Code == "AKR_DELIVERY_PROGRESS").Label.Should().Be("What progress have you made?");
    }

    // BLUM-8090: a Joint partnership has no measures, so — like People and roles — it is asked the three delivery
    // questions alone, under the same codes, flags and limits, but in the partnership wording of the Figma (node
    // 3264-356641). It is always Active, which is the phrasing the designer asked to have checked: the delivery
    // questions have no status variants, so Active changes nothing here.
    [Fact]
    public async Task JointPartnershipActivity_GetsOnlyTheDeliveryQuestions_InThePartnershipWording()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartReportAsync();
        var activity = await CreateCompletedActivityAsync(taskName: "Joint partnership", expectedResults: []);

        var dto = await GetActivityQuestionsAsync(collectionId, activity.Id);

        dto.ActivityStatus.Should().Be("active");
        dto.Questions.Should().OnlyContain(q => q.Section == "delivery");
        dto.Questions
            .Select(q => (q.Code, q.Label, q.Hint, q.IsRequired, q.MaxLength))
            .Should().Equal(
                ("AKR_DELIVERY_PROGRESS", "What progress have you made this quarter?", "Tell us about where you're at in the partnership process", true, 500),
                ("AKR_DELIVERY_CHALLENGES", "What challenges have you encountered? (optional)", "Use this space to add any information about your challenges so far", false, 500),
                ("AKR_DELIVERY_NEXT_QUARTER", "What are you planning to do next quarter? (optional)", "Use this space to add any information about your plans for the next quarter", false, 500));
    }

    [Fact]
    public async Task JointPartnershipReport_CompletedWithoutProgress_IsRefusedInThePartnershipWording()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartReportAsync();
        var activity = await CreateCompletedActivityAsync(taskName: "Joint partnership", expectedResults: []);
        await ConfirmActivitiesAsync(collectionId);
        (await SaveAnswersAsync(collectionId, activity.Id, saveAsDraft: true, ("AKR_DELIVERY_CHALLENGES", "Agreeing the split took longer than planned")))
            .EnsureSuccessStatusCode();

        var response = await CompleteActivityAsync(collectionId, activity.Id);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var envelope = await response.ToResponseModel<AnswersInvalidEnvelope>();
        envelope.AnswerErrors.Should().ContainSingle(e => e.QuestionCode == "AKR_DELIVERY_PROGRESS")
            .Which.Message.Should().Be("Enter a description of the progress you have made this quarter");
    }

    [Fact]
    public async Task WhoWithActivityAnswers_PrefillTheContributorQuestions()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartReportAsync();
        var (_, taskId) = await GetTypeAsync(RnaTaskName);
        var results = await StandardResultsAsync(taskId, "rna_contributors", "rna_published");

        // The RNA type sits in the "Needs assessment" category, whose seeded form schema prefixes codes with NA_.
        var activity = await CreateCompletedActivityAsync(
            RnaTaskName,
            "active",
            results,
            answers:
            [
                new ActivityAnswerInputDto("NA_WHO_WITH", "licensing,other"),
                new ActivityAnswerInputDto("NA_WHO_WITH_OTHER", "Partner org"),
            ]);

        var dto = await GetActivityQuestionsAsync(collectionId, activity.Id);

        dto.Answers.Should().Contain(a => a.QuestionCode == "RNA_CONTRIBUTORS" && a.Value == "licensing,other");
        dto.Answers.Should().Contain(a => a.QuestionCode == "RNA_CONTRIBUTORS_OTHER" && a.Value == "Partner org");
    }

    // BLUM-7645: BLUM-7475 turned this question into a checkbox list over the shared "Who will you work with?"
    // options but deliberately left it prefill-free, so an LA that had already named its partners when it created
    // the activity had to re-key them here. The Closed/Finished variant now pulls them through like the contributor
    // questions do. Prefill is keyed off the question, not the category, so it reaches every WhoWith-optioned
    // screening question regardless of which prefix the activity schema uses.
    [Fact]
    public async Task WhoWithActivityAnswers_PrefillTheClosedScreeningServicesQuestions()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartReportAsync();
        var (_, taskId) = await GetTypeAsync(ScreeningTaskName);
        var results = await StandardResultsAsync(taskId, "scr_tool");

        // The screening type sits in the "Help, advice and access to services" category, whose seeded form schema
        // prefixes codes with HELP_ — BuildPrefill matches the source answer by suffix, so the prefix is irrelevant.
        var activity = await CreateCompletedActivityAsync(
            ScreeningTaskName,
            "closed",
            results,
            answers:
            [
                new ActivityAnswerInputDto("HELP_WHO_WITH", "police,mental_health,other"),
                new ActivityAnswerInputDto("HELP_WHO_WITH_OTHER", "Local gambling support charity"),
            ]);

        var dto = await GetActivityQuestionsAsync(collectionId, activity.Id);

        dto.Answers.Should().Contain(a => a.QuestionCode == "SCR_SERVICES" && a.Value == "police,mental_health,other");
        dto.Answers.Should().Contain(a => a.QuestionCode == "SCR_SERVICES_OTHER" && a.Value == "Local gambling support charity");
    }

    // Where BLUM-7645 meets BLUM-7671. A finished activity carries its previously submitted answers into the next
    // quarter as real saved rows at confirm time, and the prefill only seeds codes that have no saved row — so what
    // the LA actually reported last quarter outranks the activity record's "Who will you work with?" list, which by
    // then may name partners that never screened. Reversing the merge would silently overwrite a submitted answer.
    [Fact]
    public async Task FinishedScreeningActivity_PrefersItsCarriedAnswerOverTheWhoWithPrefill()
    {
        AuthenticateAsMainOrgLead();
        var (_, taskId) = await GetTypeAsync(ScreeningTaskName);
        var results = await StandardResultsAsync(taskId, "scr_tool");
        var activity = await CreateCompletedActivityAsync(
            ScreeningTaskName,
            "finished",
            results,
            answers: [new ActivityAnswerInputDto("HELP_WHO_WITH", "licensing")]);

        var q1 = await CreateOpenQuarterCollectionAsync(1, "2026/27 Q1 quarterly report");
        await CreateSubmissionAsync(q1);
        await ConfirmActivitiesAsync(q1);
        (await SaveAnswersAsync(
            q1,
            activity.Id,
            saveAsDraft: true,
            ("SCR_SERVICES", "police"),
            ("AKR_DELIVERY_PROGRESS", "Q1 progress."))).EnsureSuccessStatusCode();
        (await CompleteActivityAsync(q1, activity.Id)).EnsureSuccessStatusCode();
        await SubmitStartedReportAsync(q1);

        var q2 = await CreateOpenQuarterCollectionAsync(2, "2026/27 Q2 quarterly report");
        await CreateSubmissionAsync(q2);
        await ConfirmActivitiesAsync(q2);

        var dto = await GetActivityQuestionsAsync(q2, activity.Id);

        dto.Answers.Should().Contain(a => a.QuestionCode == "SCR_SERVICES" && a.Value == "police");
    }

    [Fact]
    public async Task SavedReportAnswers_TakePrecedenceOverThePrefill()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartReportAsync();
        var (_, taskId) = await GetTypeAsync(RnaTaskName);
        var results = await StandardResultsAsync(taskId, "rna_contributors", "rna_published");
        var activity = await CreateCompletedActivityAsync(
            RnaTaskName,
            "active",
            results,
            answers: [new ActivityAnswerInputDto("NA_WHO_WITH", "licensing")]);
        await ConfirmActivitiesAsync(collectionId);
        (await SaveAnswersAsync(collectionId, activity.Id, saveAsDraft: true, ("RNA_CONTRIBUTORS", "police")))
            .EnsureSuccessStatusCode();

        var dto = await GetActivityQuestionsAsync(collectionId, activity.Id);

        dto.Answers.Should().Contain(a => a.QuestionCode == "RNA_CONTRIBUTORS" && a.Value == "police");
    }

    // --- BLUM-7671: a finished activity carries its previously submitted answers into later reports ---
    //
    // The carry-forward is a WRITE at confirm-activities, not an overlay on this read, so every case below must
    // confirm the later quarter's activity list before the GET. That mirrors the real UI, which locks every
    // section until the list is confirmed. CompleteFinishedRnaReportAsync / SubmitStartedReportAsync live on
    // QuarterlyReportTestBase — ConfirmActivitiesTests and RegenerateReportSnapshotsTests cover their own angles
    // on the same feature.

    // AC1: the results submitted in the quarter the activity was finished pre-populate the following quarter.
    [Fact]
    public async Task FinishedActivity_CarriesForwardTheAnswersFromTheLastSubmittedReport()
    {
        AuthenticateAsMainOrgLead();
        var activity = await CreateRnaActivityAsync(status: "finished");

        var q1 = await CreateOpenQuarterCollectionAsync(1, "2026/27 Q1 quarterly report");
        await CreateSubmissionAsync(q1);
        await ConfirmActivitiesAsync(q1);
        await CompleteFinishedRnaReportAsync(q1, activity.Id, "https://example.org/rna", "Assessment signed off in Q1.");
        await SubmitStartedReportAsync(q1);

        var q2 = await CreateOpenQuarterCollectionAsync(2, "2026/27 Q2 quarterly report");
        await CreateSubmissionAsync(q2);
        await ConfirmActivitiesAsync(q2);

        var dto = await GetActivityQuestionsAsync(q2, activity.Id);

        // No SubmissionActivityReport row is written, so the task list still reads Not started — the LA has not
        // opened the section, and claiming progress for one they have not seen would be a lie.
        dto.ReportStatus.Should().Be("not_started");
        dto.Answers.Should().Contain(a => a.QuestionCode == "RNA_PUBLISHED" && a.Value == "yes");
        dto.Answers.Should().Contain(a => a.QuestionCode == "RNA_PUBLISHED_URL" && a.Value == "https://example.org/rna");

        // The whole form carries, not just the protocol questions — the delivery narrative comes too.
        dto.Answers.Should().Contain(a => a.QuestionCode == "AKR_DELIVERY_PROGRESS" && a.Value == "Assessment signed off in Q1.");

        // The values are real answer rows the LA owns, not a read-time overlay — that is what makes them
        // clearable, and what keeps a finalised report showing what it actually contains.
        var submissionId = await GetSubmissionIdAsync(q2);
        var saved = await Context.SubmissionActivityAnswers
            .AsNoTracking()
            .Where(a => a.SubmissionId == submissionId && a.ActivityId == new ActivityId(activity.Id))
            .ToListAsync();
        saved.Should().Contain(a => a.QuestionCode == "AKR_DELIVERY_PROGRESS" && a.Value == "Assessment signed off in Q1.");
    }

    // AC2: "any subsequent quarterly report", and the most recent earlier report wins where both have a value.
    [Fact]
    public async Task FinishedActivity_CarriesForwardTheMostRecentEarlierReport()
    {
        AuthenticateAsMainOrgLead();
        var activity = await CreateRnaActivityAsync(status: "finished");

        var q1 = await CreateOpenQuarterCollectionAsync(1, "2026/27 Q1 quarterly report");
        await CreateSubmissionAsync(q1);
        await ConfirmActivitiesAsync(q1);
        await CompleteFinishedRnaReportAsync(q1, activity.Id, "https://example.org/first", "Q1 progress.");
        await SubmitStartedReportAsync(q1);

        var q2 = await CreateOpenQuarterCollectionAsync(2, "2026/27 Q2 quarterly report");
        await CreateSubmissionAsync(q2);
        await ConfirmActivitiesAsync(q2);
        await CompleteFinishedRnaReportAsync(q2, activity.Id, "https://example.org/second", "Q2 progress.");
        await SubmitStartedReportAsync(q2);

        var q3 = await CreateOpenQuarterCollectionAsync(3, "2026/27 Q3 quarterly report");
        await CreateSubmissionAsync(q3);
        await ConfirmActivitiesAsync(q3);

        var dto = await GetActivityQuestionsAsync(q3, activity.Id);

        dto.Answers.Should().Contain(a => a.QuestionCode == "RNA_PUBLISHED_URL" && a.Value == "https://example.org/second");
        dto.Answers.Should().Contain(a => a.QuestionCode == "AKR_DELIVERY_PROGRESS" && a.Value == "Q2 progress.");
    }

    // AC2 in the ordering confirm-time alone cannot serve. Two quarterly windows can be open at once (BLUM-6950),
    // so the LA can confirm Q2's list before finalising Q1 — at which point Q1 is not yet "previously submitted
    // data" and nothing carries. Confirming is a one-shot and submitting Q1 changes no activity, so no review flag
    // would ever ask them to reconfirm Q2; without the copy running again at submit time they would simply find Q2
    // blank and re-key it, which is the toil this ticket exists to remove.
    [Fact]
    public async Task FinishedActivity_CarriesForwardWhenTheEarlierReportIsSubmittedAfterTheLaterOneWasConfirmed()
    {
        AuthenticateAsMainOrgLead();
        var activity = await CreateRnaActivityAsync(status: "finished");

        var q1 = await CreateOpenQuarterCollectionAsync(1, "2026/27 Q1 quarterly report");
        var q2 = await CreateOpenQuarterCollectionAsync(2, "2026/27 Q2 quarterly report");

        // Q2 is started and confirmed first, while Q1 is still in flight — nothing to carry yet.
        AuthenticateAsMainOrgLead();
        await CreateSubmissionAsync(q2);
        await ConfirmActivitiesAsync(q2);
        (await GetActivityQuestionsAsync(q2, activity.Id)).Answers
            .Should().NotContain(a => a.QuestionCode == "AKR_DELIVERY_PROGRESS", "Q1 has not been submitted yet");

        await CreateSubmissionAsync(q1);
        await ConfirmActivitiesAsync(q1);
        await CompleteFinishedRnaReportAsync(q1, activity.Id, "https://example.org/rna", "Q1 progress.");
        await SubmitStartedReportAsync(q1);

        var dto = await GetActivityQuestionsAsync(q2, activity.Id);

        dto.Answers.Should().Contain(a => a.QuestionCode == "AKR_DELIVERY_PROGRESS" && a.Value == "Q1 progress.");
        dto.Answers.Should().Contain(a => a.QuestionCode == "RNA_PUBLISHED_URL" && a.Value == "https://example.org/rna");
    }

    // The carry-forward takes ONE report's answer set whole. A blank is never persisted, so a code the LA
    // deliberately cleared in Q2 is indistinguishable from one it never answered — filling the gap from Q1 would
    // resurrect data the LA had deleted, one quarter later and with no way to stop it.
    [Fact]
    public async Task FinishedActivity_DoesNotFallBackToAnOlderReportForACodeClearedInTheMostRecentOne()
    {
        AuthenticateAsMainOrgLead();
        var activity = await CreateRnaActivityAsync(status: "finished");

        var q1 = await CreateOpenQuarterCollectionAsync(1, "2026/27 Q1 quarterly report");
        await CreateSubmissionAsync(q1);
        await ConfirmActivitiesAsync(q1);
        await CompleteFinishedRnaReportAsync(q1, activity.Id, "https://example.org/first", "Q1 progress.", "Q1 next steps.");
        await SubmitStartedReportAsync(q1);

        // Q2 inherits Q1's answers at confirm; the LA deletes the forward-look narrative and submits without it.
        var q2 = await CreateOpenQuarterCollectionAsync(2, "2026/27 Q2 quarterly report");
        await CreateSubmissionAsync(q2);
        await ConfirmActivitiesAsync(q2);
        (await SaveAnswersAsync(q2, activity.Id, saveAsDraft: true, ("AKR_DELIVERY_NEXT_QUARTER", null)))
            .EnsureSuccessStatusCode();
        (await CompleteActivityAsync(q2, activity.Id)).EnsureSuccessStatusCode();
        await SubmitStartedReportAsync(q2);

        var q3 = await CreateOpenQuarterCollectionAsync(3, "2026/27 Q3 quarterly report");
        await CreateSubmissionAsync(q3);
        await ConfirmActivitiesAsync(q3);

        var dto = await GetActivityQuestionsAsync(q3, activity.Id);

        dto.Answers.Should().NotContain(a => a.QuestionCode == "AKR_DELIVERY_NEXT_QUARTER");

        // Everything Q2 did carry still arrives — the source report is Q2 whole, not a per-code merge.
        dto.Answers.Should().Contain(a => a.QuestionCode == "AKR_DELIVERY_PROGRESS" && a.Value == "Q1 progress.");
        dto.Answers.Should().Contain(a => a.QuestionCode == "RNA_PUBLISHED_URL" && a.Value == "https://example.org/first");
    }

    // The regression this whole change exists for. Under the old read-time overlay a carried value could not be
    // deleted: the blank was never persisted, so the row went away and the next GET re-injected it — and the LA
    // could submit it to DHSC that way.
    [Fact]
    public async Task FinishedActivity_ClearingACarriedAnswerSticks()
    {
        AuthenticateAsMainOrgLead();
        var activity = await CreateRnaActivityAsync(status: "finished");

        var q1 = await CreateOpenQuarterCollectionAsync(1, "2026/27 Q1 quarterly report");
        await CreateSubmissionAsync(q1);
        await ConfirmActivitiesAsync(q1);
        await CompleteFinishedRnaReportAsync(q1, activity.Id, "https://example.org/rna", "Q1 progress.", "Q1 next steps.");
        await SubmitStartedReportAsync(q1);

        var q2 = await CreateOpenQuarterCollectionAsync(2, "2026/27 Q2 quarterly report");
        await CreateSubmissionAsync(q2);
        await ConfirmActivitiesAsync(q2);
        (await GetActivityQuestionsAsync(q2, activity.Id)).Answers
            .Should().Contain(a => a.QuestionCode == "AKR_DELIVERY_NEXT_QUARTER" && a.Value == "Q1 next steps.");

        (await SaveAnswersAsync(q2, activity.Id, saveAsDraft: true, ("AKR_DELIVERY_NEXT_QUARTER", null)))
            .EnsureSuccessStatusCode();

        (await GetActivityQuestionsAsync(q2, activity.Id)).Answers
            .Should().NotContain(a => a.QuestionCode == "AKR_DELIVERY_NEXT_QUARTER");

        // Re-confirming after clearing is covered by ConfirmActivitiesTests, which owns the copy's guards.
    }

    // The scope decision: an in-flight activity's results are expected to move each quarter, so seeding last
    // quarter's would invite an unchanged resubmission. Only "finished" carries.
    [Fact]
    public async Task ActiveActivity_DoesNotCarryForwardPreviousAnswers()
    {
        AuthenticateAsMainOrgLead();
        var activity = await CreateRnaActivityAsync();

        var q1 = await CreateOpenQuarterCollectionAsync(1, "2026/27 Q1 quarterly report");
        await CreateSubmissionAsync(q1);
        await ConfirmActivitiesAsync(q1);
        await CompleteActivityReportAsync(q1, activity.Id);
        await SubmitStartedReportAsync(q1);

        var q2 = await CreateOpenQuarterCollectionAsync(2, "2026/27 Q2 quarterly report");
        await CreateSubmissionAsync(q2);
        await ConfirmActivitiesAsync(q2);

        var dto = await GetActivityQuestionsAsync(q2, activity.Id);

        // AKR_DELIVERY_PROGRESS has no prefill source, so with no carry-forward it has no value at all.
        dto.Answers.Should().NotContain(a => a.QuestionCode == "AKR_DELIVERY_PROGRESS");
    }

    // A started-but-never-finalised report is not "previously submitted data" — and because two quarterly
    // windows can be open at once (BLUM-6950), the other window's draft must not leak into this one.
    [Fact]
    public async Task FinishedActivity_DoesNotCarryForwardAReportThatWasNeverSubmitted()
    {
        AuthenticateAsMainOrgLead();
        var activity = await CreateRnaActivityAsync(status: "finished");

        var q1 = await CreateOpenQuarterCollectionAsync(1, "2026/27 Q1 quarterly report");
        await CreateSubmissionAsync(q1);
        await ConfirmActivitiesAsync(q1);
        await CompleteFinishedRnaReportAsync(q1, activity.Id, "https://example.org/draft", "Never submitted.");

        var q2 = await CreateOpenQuarterCollectionAsync(2, "2026/27 Q2 quarterly report");
        await CreateSubmissionAsync(q2);
        await ConfirmActivitiesAsync(q2);

        var dto = await GetActivityQuestionsAsync(q2, activity.Id);

        dto.Answers.Should().NotContain(a => a.QuestionCode == "AKR_DELIVERY_PROGRESS");
        dto.Answers.Should().NotContain(a => a.QuestionCode == "RNA_PUBLISHED_URL");
    }

    // The carried values are a starting point, not a fixture: a later save overwrites the one code it submits and
    // leaves the rest of the carried set alone (the per-page merge contract).
    [Fact]
    public async Task SavedAnswersInTheCurrentReport_TakePrecedenceOverTheCarriedForwardValues()
    {
        AuthenticateAsMainOrgLead();
        var activity = await CreateRnaActivityAsync(status: "finished");

        var q1 = await CreateOpenQuarterCollectionAsync(1, "2026/27 Q1 quarterly report");
        await CreateSubmissionAsync(q1);
        await ConfirmActivitiesAsync(q1);
        await CompleteFinishedRnaReportAsync(q1, activity.Id, "https://example.org/rna", "Q1 progress.");
        await SubmitStartedReportAsync(q1);

        var q2 = await CreateOpenQuarterCollectionAsync(2, "2026/27 Q2 quarterly report");
        await CreateSubmissionAsync(q2);
        await ConfirmActivitiesAsync(q2);
        (await SaveAnswersAsync(q2, activity.Id, saveAsDraft: true, ("AKR_DELIVERY_PROGRESS", "Revised for Q2.")))
            .EnsureSuccessStatusCode();

        var dto = await GetActivityQuestionsAsync(q2, activity.Id);

        dto.Answers.Should().Contain(a => a.QuestionCode == "AKR_DELIVERY_PROGRESS" && a.Value == "Revised for Q2.");

        // Untouched codes still carry.
        dto.Answers.Should().Contain(a => a.QuestionCode == "RNA_PUBLISHED_URL" && a.Value == "https://example.org/rna");
    }
}