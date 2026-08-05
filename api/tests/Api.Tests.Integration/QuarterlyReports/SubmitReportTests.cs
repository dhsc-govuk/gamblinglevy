using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using Api.Dtos;
using Domain.Collections;
using Domain.Users;
using Domain.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Tests.Common.Services;
using Xunit;

namespace Api.Tests.Integration.QuarterlyReports;

// Report finalisation: the "Submit your quarterly report" command (reference-number generation + locking) and the
// "Download your report" ZIP export. A report is submittable only once all three sections are completed.
public class SubmitReportTests(IntegrationTestWebFactory factory) : QuarterlyReportTestBase(factory)
{
    private sealed record ErrorEnvelope(string Error, string Message);

    // The subset of the context body this class asserts on. Dates deserialise as DateTime — Newtonsoft cannot bind
    // DateOnly (the ReportContextTests precedent).
    private sealed record ContextResult(Guid CollectionId, int Quarter, Guid? SubmissionId, DateTime? SubmittedAt);

    private Task<HttpResponseMessage> SubmitReportAsync(Guid collectionId) =>
        Client.PostAsync($"quarterly-report/{collectionId}/submission/submit", null);

    // Starts + confirms the report and completes all three sections as the current identity. Creates the activity to
    // report on unless the caller supplies one — two open collections must report on the SAME activity, since creating
    // a second one after a list is confirmed flags that report for review.
    private async Task CompleteAllSectionsAsync(Guid collectionId, Guid? activityId = null)
    {
        var activity = activityId ?? (await CreateRnaActivityAsync()).Id;
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        await CompleteActivityReportAsync(collectionId, activity);
        await CompleteFundingSpendSectionAsync(collectionId);
        await CompleteStrategicFeedbackSectionAsync(collectionId);
    }

    [Fact]
    public async Task SubmitReport_WhenAllSectionsComplete_ReturnsReferenceAndLocksReport()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        await CompleteAllSectionsAsync(collectionId);

        // The task list should now allow submission.
        var taskList = await GetTaskListAsync(collectionId);
        taskList.CanSubmit.Should().BeTrue();
        taskList.Submitted.Should().BeFalse();

        var response = await SubmitReportAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.ToResponseModel<SubmitReportResultDto>();
        // FY 2026/27, quarter 1 => prefix 2027 + 01; first submission for the collection => 0001.
        result.ReferenceNumber.Should().Be("202701-0001");
        Regex.IsMatch(result.ReferenceNumber, "^\\d{6}-\\d{4}$").Should().BeTrue();

        var submission = await Context.Submissions.AsNoTracking()
            .SingleAsync(s => s.CollectionId == new CollectionId(collectionId));
        submission.SubmittedAt.Should().NotBeNull();
        submission.ReferenceNumber.Should().Be("202701-0001");

        // The task list now reports the read-only submitted state.
        var afterTaskList = await GetTaskListAsync(collectionId);
        afterTaskList.Submitted.Should().BeTrue();
        afterTaskList.CanSubmit.Should().BeFalse();
        afterTaskList.ReferenceNumber.Should().Be("202701-0001");
    }

    // BLUM-6954: submitting sends a confirmation email to the submitting LA user's registered address. The submitter
    // is resolved from a real users row (as in production), so seed one for the acting Lead identity.
    private const string MainOrgLeadEmail = "lead.user@example.gov.uk";

    private async Task EnsureMainOrgLeadUserAsync()
    {
        var leadId = new UserId(MainOrgLeadUserId);
        if (await Context.Users.AsNoTracking().AnyAsync(u => u.Id == leadId))
        {
            return;
        }

        var user = User.New(
            leadId,
            new Name("Lead", "User"),
            MainOrgLeadEmail,
            new SubId(Guid.NewGuid()),
            UserStatus.Active,
            UserRole.Lead);
        await Context.Users.AddAsync(user);
        await SaveChangesAsync();
    }

    [Fact]
    public async Task SubmitReport_WhenSubmitted_SendsConfirmationEmailToSubmitter()
    {
        InMemorySubmissionConfirmationEmailService.Clear();
        await EnsureMainOrgLeadUserAsync();

        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        await CompleteAllSectionsAsync(collectionId);

        var response = await SubmitReportAsync(collectionId);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.ToResponseModel<SubmitReportResultDto>();

        // The email address is unique to this test's seeded user, so a match is unambiguous even if other test
        // classes push entries onto the shared (static) recorder in parallel.
        InMemorySubmissionConfirmationEmailService.Sent
            .Should().Contain(s => s.Email == MainOrgLeadEmail && s.ReferenceNumber == result.ReferenceNumber);
    }

    [Fact]
    public async Task SubmitReport_WhenSectionsIncomplete_Returns422ReportIncomplete()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        // No section completed.

        var response = await SubmitReportAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var envelope = await response.ToResponseModel<ErrorEnvelope>();
        envelope.Error.Should().Be("REPORT_INCOMPLETE");
    }

    [Fact]
    public async Task SubmitReport_WhenActivitiesNotConfirmed_Returns422ActivitiesNotConfirmed()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        // Not confirmed.

        var response = await SubmitReportAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var envelope = await response.ToResponseModel<ErrorEnvelope>();
        envelope.Error.Should().Be("ACTIVITIES_NOT_CONFIRMED");
    }

    [Fact]
    public async Task SubmitReport_WhenAlreadySubmitted_Returns409()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        await CompleteAllSectionsAsync(collectionId);
        (await SubmitReportAsync(collectionId)).EnsureSuccessStatusCode();

        var response = await SubmitReportAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task SubmitReport_ReferenceSequenceIncrementsAcrossLocalAuthorities()
    {
        var collectionId = await CreateOpenCollectionAsync(localAuthorityIds: [MainOrg.Id.Value, SecondOrg.Id.Value]);

        AuthenticateAsMainOrgLead();
        await CompleteAllSectionsAsync(collectionId);
        var first = await (await SubmitReportAsync(collectionId)).ToResponseModel<SubmitReportResultDto>();

        AuthenticateAsSecondOrgLead();
        await CompleteAllSectionsAsync(collectionId);
        var second = await (await SubmitReportAsync(collectionId)).ToResponseModel<SubmitReportResultDto>();

        first.ReferenceNumber.Should().Be("202701-0001");
        second.ReferenceNumber.Should().Be("202701-0002");
    }

    // The BLUM-6951 regression. An organisation can have two quarterly windows open at once, and submitting one must
    // leave the other alone. The pre-BLUM-6950 flow resolved "the organisation's first open quarterly collection"
    // server-side whatever row the user clicked, so once one report was submitted every other open collection
    // rendered as submitted too.
    [Fact]
    public async Task SubmitReport_WithTwoOpenCollections_DoesNotMarkTheOtherAsSubmitted()
    {
        AuthenticateAsMainOrgLead();
        var q1Id = await CreateOpenQuarterCollectionAsync(1, "2026 Q1 quarterly report");
        var q2Id = await CreateOpenQuarterCollectionAsync(2, "2026 Q2 quarterly report");

        // Both windows report on the same activity: a second activity would flag the first (confirmed) report for review.
        var activity = await CreateRnaActivityAsync();
        await CompleteAllSectionsAsync(q1Id, activity.Id);
        (await SubmitReportAsync(q1Id)).EnsureSuccessStatusCode();

        // The Reports landing derives each row's tag from this list: only the submitted window is Completed.
        var collections = await (await Client.GetAsync("quarterly-report/collections"))
            .ToResponseModel<List<MyCollectionSummaryDto>>();

        var q1Row = collections.Single(c => c.CollectionId == q1Id);
        q1Row.SubmissionStatus.Should().Be("completed");
        q1Row.SubmittedAt.Should().NotBeNull();

        var q2Row = collections.Single(c => c.CollectionId == q2Id);
        q2Row.CollectionStatus.Should().Be("open");
        q2Row.SubmissionStatus.Should().Be("not_started");
        q2Row.SubmittedAt.Should().BeNull();

        // Opening the other window returns ITS report, not the submitted one.
        var context = await (await Client.GetAsync($"quarterly-report/{q2Id}/context")).ToResponseModel<ContextResult>();
        context.CollectionId.Should().Be(q2Id);
        context.Quarter.Should().Be(2);
        context.SubmissionId.Should().BeNull();
        context.SubmittedAt.Should().BeNull();

        // Exactly one submission row is finalised.
        var finalised = await Context.Submissions.AsNoTracking().Where(s => s.SubmittedAt != null).ToListAsync();
        finalised.Should().ContainSingle().Which.CollectionId.Should().Be(new CollectionId(q1Id));
        var q1SubmittedAt = finalised.Single().SubmittedAt;

        // The other window is still authorable and submits independently, on its own reference sequence.
        await CompleteAllSectionsAsync(q2Id, activity.Id);
        var q2Result = await (await SubmitReportAsync(q2Id)).ToResponseModel<SubmitReportResultDto>();
        q2Result.ReferenceNumber.Should().Be("202702-0001");

        // …and the first report is untouched by it.
        var q1Submission = await Context.Submissions.AsNoTracking()
            .SingleAsync(s => s.CollectionId == new CollectionId(q1Id));
        q1Submission.ReferenceNumber.Should().Be("202701-0001");
        q1Submission.SubmittedAt.Should().Be(q1SubmittedAt);
    }

    [Fact]
    public async Task SubmittedReport_RejectsFurtherEdits()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        var activity = await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        await CompleteActivityReportAsync(collectionId, activity.Id);
        await CompleteFundingSpendSectionAsync(collectionId);
        await CompleteStrategicFeedbackSectionAsync(collectionId);
        (await SubmitReportAsync(collectionId)).EnsureSuccessStatusCode();

        // Every write against a submitted report is rejected (409).
        var saveAnswers = await SaveAnswersAsync(collectionId, activity.Id, saveAsDraft: true, ("SF_NOT_A_REAL_CODE", "x"));
        saveAnswers.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var confirm = await Client.PostAsync($"quarterly-report/{collectionId}/confirm-activities", null);
        confirm.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var completeFeedback = await CompleteStrategicFeedbackAsync(collectionId);
        completeFeedback.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Export_AfterSubmit_ReturnsZipWithSectionCsvs()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        await CompleteAllSectionsAsync(collectionId);
        (await SubmitReportAsync(collectionId)).EnsureSuccessStatusCode();

        var response = await Client.GetAsync($"quarterly-report/{collectionId}/export");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().NotBeEmpty();

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        archive.Entries.Select(e => e.Name).Should().BeEquivalentTo(
            "report-summary.csv", "activity-key-results.csv", "funding-spend.csv", "strategic-feedback.csv");
    }

    [Fact]
    public async Task Export_BeforeSubmit_Returns404()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        await CompleteAllSectionsAsync(collectionId);
        // Not submitted.

        var response = await Client.GetAsync($"quarterly-report/{collectionId}/export");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}