using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using Api.Dtos;
using Application.AuditLogs;
using Application.AuditLogs.Dtos;
using Domain.AuditLogs;
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

    // An LA that pools all of its levy money into joint partnerships. BLUM-7860 kept partnerships out of "Enter
    // activity results", so confirming rolled that section straight to Completed for this organisation; BLUM-8090
    // moved the partnership's progress questions into the report, so it now has a delivery report to complete like
    // any other activity — and the section, and with it the submission, waits for it.
    [Fact]
    public async Task SubmitReport_WhenTheOnlyActivityIsAJointPartnership_WaitsForItsDeliveryReport()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        var jointPartnership = await CreateCompletedActivityAsync(taskName: "Joint partnership", expectedResults: []);
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        await CompleteFundingSpendSectionAsync(collectionId);
        await CompleteStrategicFeedbackSectionAsync(collectionId);

        var beforeReporting = await GetTaskListAsync(collectionId);
        beforeReporting.Sections.Single(s => s.ModuleCode == KeyResultsModuleCode).Status.Should().Be("not_started");
        beforeReporting.CanSubmit.Should().BeFalse();
        (await SubmitReportAsync(collectionId)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        await CompleteActivityReportAsync(collectionId, jointPartnership.Id);

        var taskList = await GetTaskListAsync(collectionId);
        taskList.Sections.Single(s => s.ModuleCode == KeyResultsModuleCode).Status.Should().Be("completed");
        taskList.CanSubmit.Should().BeTrue();

        var response = await SubmitReportAsync(collectionId);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var spend = await Context.SubmissionSpendEntries.AsNoTracking().ToListAsync();
        spend.Should().Contain(e => e.ActivityId == new Domain.Activities.ActivityId(jointPartnership.Id));
    }

    // Alongside another activity, a partnership's report is owed as well as that activity's: reporting on the other
    // one alone does not complete the section.
    [Fact]
    public async Task SubmitReport_WhenAReportableActivityIsUnreported_IsStillBlocked()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        await CreateCompletedActivityAsync(taskName: "Joint partnership", expectedResults: []);
        var rna = await CreateRnaActivityAsync(title: "Regional needs assessment");
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        await CompleteActivityReportAsync(collectionId, rna.Id);
        await CompleteFundingSpendSectionAsync(collectionId);
        await CompleteStrategicFeedbackSectionAsync(collectionId);

        var taskList = await GetTaskListAsync(collectionId);
        taskList.Sections.Single(s => s.ModuleCode == KeyResultsModuleCode).Status.Should().NotBe("completed");
        taskList.CanSubmit.Should().BeFalse();

        var response = await SubmitReportAsync(collectionId);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
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
        // The prefix is the financial year's END year plus the quarter; first submission => 0001. Derived rather
        // than written out, so the assertion survives the year turning over — SubmissionReferenceTests (unit) is
        // what pins the format itself against literals.
        result.ReferenceNumber.Should().Be(TestFinancialYears.Reference(TestFinancialYears.Current, 1, 1));
        Regex.IsMatch(result.ReferenceNumber, "^\\d{6}-\\d{4}$").Should().BeTrue();

        var submission = await Context.Submissions.AsNoTracking()
            .SingleAsync(s => s.CollectionId == new CollectionId(collectionId));
        submission.SubmittedAt.Should().NotBeNull();
        submission.ReferenceNumber.Should().Be(TestFinancialYears.Reference(TestFinancialYears.Current, 1, 1));

        // The task list now reports the read-only submitted state.
        var afterTaskList = await GetTaskListAsync(collectionId);
        afterTaskList.Submitted.Should().BeTrue();
        afterTaskList.CanSubmit.Should().BeFalse();
        afterTaskList.ReferenceNumber.Should().Be(TestFinancialYears.Reference(TestFinancialYears.Current, 1, 1));
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
            UserRole.Lead,
            DateTime.UtcNow);
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

        first.ReferenceNumber.Should().Be(TestFinancialYears.Reference(TestFinancialYears.Current, 1, 1));
        second.ReferenceNumber.Should().Be(TestFinancialYears.Reference(TestFinancialYears.Current, 1, 2));
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
        q2Result.ReferenceNumber.Should().Be(TestFinancialYears.Reference(TestFinancialYears.Current, 2, 1));

        // …and the first report is untouched by it.
        var q1Submission = await Context.Submissions.AsNoTracking()
            .SingleAsync(s => s.CollectionId == new CollectionId(q1Id));
        q1Submission.ReferenceNumber.Should().Be(TestFinancialYears.Reference(TestFinancialYears.Current, 1, 1));
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
            "report-summary.csv", "activity-results.csv", "funding-spend.csv", "strategic-feedback.csv");
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

    // BLUM-8747: submitting the report is the service's core act and must leave exactly one audit row, attributed to
    // the submitting user and scoped to their LA, so that LA's own audit log can show it and no other LA's can.
    [Fact]
    public async Task SubmitReport_WritesOneSubmittedAuditRow_VisibleToItsOwnLaAdminOnly()
    {
        var mainOrgAdminUserId = Guid.Parse("b0000000-0000-0000-0000-0000000000a1");
        var secondOrgAdminUserId = Guid.Parse("b0000000-0000-0000-0000-0000000000a2");

        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        await CompleteAllSectionsAsync(collectionId);

        var response = await SubmitReportAsync(collectionId);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.ToResponseModel<SubmitReportResultDto>();
        var submissionId = await GetSubmissionIdAsync(collectionId);

        var rows = await Context.AuditLogs.AsNoTracking()
            .Where(a => a.Action == AuditAction.SubmissionSubmitted)
            .ToListAsync();
        var row = rows.Should().ContainSingle().Subject;
        row.TargetType.Should().Be(AuditTargetType.Submission);
        row.TargetId.Should().Be(submissionId.Value);
        row.ActorUserId.Should().Be(new UserId(MainOrgLeadUserId));
        row.OrganisationId.Should().Be(MainOrg.Id);
        row.After.Should().Contain(result.ReferenceNumber);

        // The LA's own admin sees it in organisation-admin/audit-logs, which scopes by OrganisationId alone.
        AuthenticateAs(Role.OrganisationAdmin, mainOrgAdminUserId, MainOrg.Id.Value);
        var ownPage = await (await Client.GetAsync($"organisation-admin/audit-logs?action={AuditAction.SubmissionSubmitted}"))
            .ToResponseModel<PagedAuditLogs<AuditLogListItemDto>>();
        ownPage.Items.Should().ContainSingle(item => item.Id == row.Id.Value && item.TargetId == submissionId.Value);

        // Another LA's admin does not, neither in the list nor by id.
        AuthenticateAs(Role.OrganisationAdmin, secondOrgAdminUserId, SecondOrg.Id.Value);
        var otherPage = await (await Client.GetAsync($"organisation-admin/audit-logs?action={AuditAction.SubmissionSubmitted}"))
            .ToResponseModel<PagedAuditLogs<AuditLogListItemDto>>();
        otherPage.Items.Should().BeEmpty();
        otherPage.TotalCount.Should().Be(0);
        (await Client.GetAsync($"organisation-admin/audit-logs/{row.Id.Value}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}