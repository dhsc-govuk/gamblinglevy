using System.Net;
using Domain.Activities;
using Domain.Submissions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.QuarterlyReports;

// POST quarterly-report/{collectionId}/confirm-activities — the "Confirm your activities for this quarter" step
// that unlocks the report sections.
public class ConfirmActivitiesTests(IntegrationTestWebFactory factory) : QuarterlyReportTestBase(factory)
{
    private Task<HttpResponseMessage> PostConfirmAsync(Guid collectionId) =>
        Client.PostAsync($"quarterly-report/{collectionId}/confirm-activities", null);

    // Row identity included deliberately: a repeat copy would keep the values and change the ids.
    private async Task<IReadOnlyList<(Guid Id, string QuestionCode, string Value, DateTime CreatedAt)>> CarriedRowsAsync(
        SubmissionId submissionId, Guid activityId)
    {
        var rows = await Context.SubmissionActivityAnswers
            .AsNoTracking()
            .Where(a => a.SubmissionId == submissionId && a.ActivityId == new ActivityId(activityId))
            .ToListAsync();

        return rows
            .Select(a => (a.Id.Value, a.QuestionCode, a.Value, a.CreatedAt))
            .OrderBy(a => a.QuestionCode, StringComparer.Ordinal)
            .ToList();
    }

    [Fact]
    public async Task Confirm_MarksActivitiesConfirmedAndUnlocksSections()
    {
        var collectionId = await CreateOpenCollectionAsync();
        AuthenticateAsMainOrgLead();
        var beforeConfirm = await CreateSubmissionAsync(collectionId);
        beforeConfirm.Sections.Should().OnlyContain(s => !s.IsAvailable);

        var response = await PostConfirmAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var taskList = await response.ToResponseModel<Api.Dtos.SubmissionTaskListDto>();
        taskList.ActivitiesConfirmed.Should().BeTrue();
        taskList.Sections.Should().OnlyContain(s => s.IsAvailable);

        var persisted = await Context.Submissions.AsNoTracking().SingleAsync();
        persisted.AreActivitiesConfirmed.Should().BeTrue();
        persisted.ActivitiesConfirmedBy!.Value.Should().Be(MainOrgLeadUserId);

        var refetched = await GetTaskListAsync(collectionId);
        refetched.ActivitiesConfirmed.Should().BeTrue();
        refetched.Sections.Should().OnlyContain(s => s.IsAvailable);
    }

    [Fact]
    public async Task Confirm_WithoutASubmission_ReturnsNotFound()
    {
        var collectionId = await CreateOpenCollectionAsync();
        AuthenticateAsMainOrgLead();

        var response = await PostConfirmAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- BLUM-7671: confirming is what seeds a finished activity's previously submitted results ---

    // Confirming twice is routine, not an edge case: the task list always renders the step as clickable and every
    // activity-list edit re-raises the "review and reconfirm each section" flag. The second confirm must leave the
    // rows entirely alone. Asserting on the values alone would not catch a regression here — ReplaceForActivityAsync
    // deletes-then-inserts, so an unguarded repeat copy writes the same values back under fresh row ids with
    // CreatedBy/CreatedAt re-stamped. The ids are what pin it.
    [Fact]
    public async Task Confirm_Twice_DoesNotDuplicateOrRewriteTheCarriedAnswers()
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

        var submissionId = await GetSubmissionIdAsync(q2);
        var afterFirst = await CarriedRowsAsync(submissionId, activity.Id);
        afterFirst.Should().NotBeEmpty("the first confirm is what seeds the carried answers");

        var second = await PostConfirmAsync(q2);

        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterSecond = await CarriedRowsAsync(submissionId, activity.Id);
        afterSecond.Should().OnlyHaveUniqueItems(a => a.QuestionCode);
        afterSecond.Should().BeEquivalentTo(afterFirst, "a repeat confirm must not touch the rows at all");
        afterSecond.Should().Contain(a => a.QuestionCode == "AKR_DELIVERY_PROGRESS" && a.Value == "Q1 progress.");
        afterSecond.Should().Contain(a => a.QuestionCode == "RNA_PUBLISHED_URL" && a.Value == "https://example.org/rna");
    }

    // PR #87 review: the carry-forward logs and swallows its own failures, but it ran inside the confirm transaction,
    // where a failed statement aborts everything — so a database fault there rolled the confirmation back, and one
    // that happens every time (a row the table refuses) left the LA unable ever to confirm or submit.
    [Fact]
    public async Task Confirm_StillConfirms_WhenTheCarryForwardCannotWriteItsAnswers()
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

        HttpResponseMessage response;
        await RejectedInserts.RejectAllAsync(Context, "submission_activity_answers");
        try
        {
            response = await PostConfirmAsync(q2);
        }
        finally
        {
            await RejectedInserts.RestoreAsync(Context);
        }

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var submissionId = await GetSubmissionIdAsync(q2);
        var persisted = await Context.Submissions.AsNoTracking().SingleAsync(s => s.Id == submissionId);
        persisted.AreActivitiesConfirmed.Should().BeTrue();
        (await Context.AuditLogs.AsNoTracking()
                .AnyAsync(x => x.TargetId == submissionId.Value && x.Action == Domain.AuditLogs.AuditAction.SubmissionActivitiesConfirmed))
            .Should().BeTrue();
        (await CarriedRowsAsync(submissionId, activity.Id)).Should().BeEmpty("the inserts were refused — the LA simply re-keys, as before");
    }

    // The LA's own edit outranks the copy. Re-confirming after they have started the section must leave what they
    // typed alone — a carried value is a starting point they own, not a fixture reapplied on every confirm.
    [Fact]
    public async Task Confirm_DoesNotOverwriteAnswersTheOrganisationHasAlreadyEdited()
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

        (await PostConfirmAsync(q2)).StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await GetActivityQuestionsAsync(q2, activity.Id);
        dto.Answers.Should().Contain(a => a.QuestionCode == "AKR_DELIVERY_PROGRESS" && a.Value == "Revised for Q2.");
    }

    // The case the report-row guard exists for, and the one the answer-row guard cannot see: the LA deletes every
    // carried value, leaving no answer rows at all, then the activity list changes and they reconfirm. Resurrecting
    // the answers here would reintroduce the un-clearable value in miniature — deletable, but only until the next
    // confirm. Every save writes a report row, so that row is the durable record that the LA has been here.
    [Fact]
    public async Task Confirm_AfterEveryCarriedAnswerWasCleared_DoesNotResurrectThem()
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

        var submissionId = await GetSubmissionIdAsync(q2);
        (await CarriedRowsAsync(submissionId, activity.Id)).Should().NotBeEmpty();

        // Clear the lot. Draft saves skip required enforcement, which is how the LA can empty a page and come back.
        (await SaveAnswersAsync(
            q2,
            activity.Id,
            saveAsDraft: true,
            ("RNA_CONTRIBUTORS", null),
            ("RNA_PUBLISHED", null),
            ("RNA_PUBLISHED_URL", null),
            ("AKR_DELIVERY_PROGRESS", null),
            ("AKR_DELIVERY_NEXT_QUARTER", null))).EnsureSuccessStatusCode();

        (await CarriedRowsAsync(submissionId, activity.Id)).Should().BeEmpty("the LA deleted every carried value");

        (await PostConfirmAsync(q2)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await CarriedRowsAsync(submissionId, activity.Id))
            .Should().BeEmpty("a reconfirm must not undo the deletion");
    }

    // Carried rows are the only ones written to submission_activity_answers outside a page save, so they run the
    // same validator. BuildCarryForward only checks the code still resolves — the value can have gone stale, which
    // a key-result protocol reseed between quarters does (BLUM-7472/7475/7563). Persisting one unchecked shows the
    // LA a value they never chose and then blocks Check-your-answers with an error on a field they never touched.
    // The stale value is faked with SQL rather than by editing the seeded protocol, which the rest of this class
    // depends on staying intact.
    [Fact]
    public async Task Confirm_DoesNotCarryAValueThatIsNoLongerValidForItsQuestion()
    {
        AuthenticateAsMainOrgLead();
        var activity = await CreateRnaActivityAsync(status: "finished");

        var q1 = await CreateOpenQuarterCollectionAsync(1, "2026/27 Q1 quarterly report");
        await CreateSubmissionAsync(q1);
        await ConfirmActivitiesAsync(q1);
        await CompleteFinishedRnaReportAsync(q1, activity.Id, "https://example.org/rna", "Q1 progress.");
        await SubmitStartedReportAsync(q1);

        // RNA_PUBLISHED is a yes/no radio; "maybe" is what a renamed option looks like a quarter later.
        var q1SubmissionId = await GetSubmissionIdAsync(q1);
        await Context.Database.ExecuteSqlRawAsync(
            "UPDATE submission_activity_answers SET value = 'maybe' WHERE submission_id = {0} AND question_code = 'RNA_PUBLISHED'",
            q1SubmissionId.Value);

        var q2 = await CreateOpenQuarterCollectionAsync(2, "2026/27 Q2 quarterly report");
        await CreateSubmissionAsync(q2);
        await ConfirmActivitiesAsync(q2);

        var carried = await CarriedRowsAsync(await GetSubmissionIdAsync(q2), activity.Id);

        carried.Should().NotContain(a => a.QuestionCode == "RNA_PUBLISHED", "the value is no longer a valid option");
        carried.Should().NotContain(
            a => a.QuestionCode == "RNA_PUBLISHED_URL",
            "dropping the parent hides its conditional child, exactly as the save path would");

        // The rest of the form still carries — only the offending answer is dropped, not the whole activity.
        carried.Should().Contain(a => a.QuestionCode == "AKR_DELIVERY_PROGRESS" && a.Value == "Q1 progress.");
    }

    // Sections are snapshotted from the collection's chosen modules at start, so a quarterly collection published
    // without the key-results module gives the LA no "Enter activity results" section at all. Rows written there
    // would be invisible and unclearable, yet the report snapshot would still export them.
    [Fact]
    public async Task Confirm_WhenTheCollectionHasNoActivityResultsSection_WritesNoAnswers()
    {
        AuthenticateAsMainOrgLead();
        var activity = await CreateRnaActivityAsync(status: "finished");

        var q1 = await CreateOpenQuarterCollectionAsync(1, "2026/27 Q1 quarterly report");
        await CreateSubmissionAsync(q1);
        await ConfirmActivitiesAsync(q1);
        await CompleteFinishedRnaReportAsync(q1, activity.Id, "https://example.org/rna", "Q1 progress.");
        await SubmitStartedReportAsync(q1);

        var withoutKeyResults = await FormModuleIdsExceptAsync(KeyResultsModuleCode);
        var q2 = await CreateQuarterlyCollectionAsync(
            "2026/27 Q2 without activity results",
            Today.AddDays(-1),
            Today.AddDays(60),
            quarter: 2,
            formModuleIds: withoutKeyResults);

        AuthenticateAsMainOrgLead();
        var taskList = await CreateSubmissionAsync(q2);
        taskList.Sections.Should().NotContain(s => s.ModuleCode == KeyResultsModuleCode);

        await ConfirmActivitiesAsync(q2);

        (await CarriedRowsAsync(await GetSubmissionIdAsync(q2), activity.Id))
            .Should().BeEmpty("there is no section for these answers to appear in or be cleared from");
    }

    // The copy is best-effort by design (it owns its own try/catch, like SubmissionReviewService): an activity
    // with nothing to carry must confirm exactly as before, with no answer rows invented for it.
    [Fact]
    public async Task Confirm_WithNoEarlierSubmittedReport_LeavesTheReportEmpty()
    {
        AuthenticateAsMainOrgLead();
        var activity = await CreateRnaActivityAsync(status: "finished");

        var collectionId = await CreateOpenQuarterCollectionAsync(1, "2026/27 Q1 quarterly report");
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);

        var submissionId = await GetSubmissionIdAsync(collectionId);
        (await Context.SubmissionActivityAnswers.AsNoTracking()
            .AnyAsync(a => a.SubmissionId == submissionId)).Should().BeFalse();

        var dto = await GetActivityQuestionsAsync(collectionId, activity.Id);
        dto.ReportStatus.Should().Be("not_started");
        dto.Answers.Should().NotContain(a => a.QuestionCode == "AKR_DELIVERY_PROGRESS");
    }
}