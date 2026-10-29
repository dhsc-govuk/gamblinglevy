using System.Net;
using Api.Dtos;
using Application.Common.ReportSnapshots;
using Application.Submissions.Commands.RegenerateReportSnapshot;
using Application.Submissions.Exceptions;
using Domain.Collections;
using Domain.DataCollectionForms;
using Domain.Organisations;
using Domain.Submissions;
using Domain.Users;
using Domain.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.QuarterlyReports;

// One-off admin repair (BLUM-7130 CSV review): re-freezes every already-submitted submission's report snapshot
// in a collection under today's rules and header shape. Fixes a collection whose DHSC-wide export silently drops
// funding-spend.csv/funding-summary.csv rows for organisations that submitted before a header-shape change — see
// ExportSubmissionsCommand.BuildFundingCsv's compatibleSources guard, which omits rather than risks misaligning
// cells. Admin-only on DataCollectionsController, mirroring DhscSubmissionExportTests' sibling coverage of the
// export action on the same controller.
public class RegenerateReportSnapshotsTests(IntegrationTestWebFactory factory) : QuarterlyReportTestBase(factory)
{
    private int seededSubmitters;

    private Task<HttpResponseMessage> RegenerateAsync(Guid collectionId) =>
        Client.PostAsync($"data-collections/{collectionId}/submissions/regenerate-snapshots", null);

    // A finalised submission with no snapshot at all — the exact "legacy pre-snapshot submission" condition
    // ExportSubmissionsCommand currently drops from every section file, and the simplest reliable way to prove
    // regeneration writes a fresh, current-shape one back. Started and submitted directly through the domain
    // factories (not the full report/activities/funding/feedback HTTP flow) because this suite is only proving
    // the regenerate mechanism itself, not report content.
    private async Task<Guid> SeedSubmittedSubmissionWithoutSnapshotAsync(Guid collectionId, Organisation org)
    {
        var sequence = ++seededSubmitters;
        var submitter = User.New(
            UserId.New(),
            new Name("Sam", "Submitter"),
            $"sam.submitter{sequence}@example.gov.uk",
            new SubId(Guid.NewGuid()),
            UserStatus.Active,
            UserRole.Lead,
            DateTime.UtcNow);
        await Context.Users.AddAsync(submitter);

        var submission = Submission.Start(
            SubmissionId.New(),
            new CollectionId(collectionId),
            org.Id,
            await AllModuleSectionsAsync(),
            submitter.Id,
            DateTime.UtcNow);
        submission.Submit(TestFinancialYears.Reference(TestFinancialYears.Current, 1, sequence), submitter.Id, DateTime.UtcNow);
        await Context.Submissions.AddAsync(submission);
        await SaveChangesAsync();

        return submission.Id.Value;
    }

    private async Task<Guid> SeedDraftSubmissionAsync(Guid collectionId, Organisation org, string emailPrefix = "drew.draft.regen")
    {
        var submitter = User.New(
            UserId.New(),
            new Name("Drew", "Draft"),
            $"{emailPrefix}{++seededSubmitters}@example.gov.uk",
            new SubId(Guid.NewGuid()),
            UserStatus.Active,
            UserRole.Lead,
            DateTime.UtcNow);
        await Context.Users.AddAsync(submitter);

        // Intentionally NOT submitted.
        var submission = Submission.Start(
            SubmissionId.New(),
            new CollectionId(collectionId),
            org.Id,
            Array.Empty<(FormModuleId ModuleId, string ModuleCode)>(),
            submitter.Id,
            DateTime.UtcNow);
        await Context.Submissions.AddAsync(submission);
        await SaveChangesAsync();

        return submission.Id.Value;
    }

    [Fact]
    public async Task RegenerateSnapshots_SubmittedSubmissionWithNoSnapshot_RegeneratesAndPersistsCurrentShape()
    {
        var collectionId = await CreateOpenCollectionAsync();
        await SeedSubmittedSubmissionWithoutSnapshotAsync(collectionId, MainOrg);

        AuthenticateAsAdmin();
        var response = await RegenerateAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.ToResponseModel<RegenerateReportSnapshotsResultDto>();
        result.Regenerated.Should().Be(1);
        result.Skipped.Should().BeEmpty();

        var saved = await Context.Submissions.AsNoTracking().FirstAsync(s => s.OrganisationId == MainOrg.Id);
        saved.ReportSnapshotJson.Should().NotBeNull("regeneration must write a snapshot back, not leave it null");

        var snapshot = ReportSnapshotJson.TryRead(saved.ReportSnapshotJson!);
        snapshot.Should().NotBeNull("the regenerated JSON must itself be a readable snapshot document");

        // Activities were never confirmed for this submission (seeded directly, bypassing that step), so a funding
        // section may legitimately be absent — but if one is present, its header must be today's shape, not stale.
        if (snapshot!.Funding is not null)
        {
            snapshot.Funding.Header.Should().BeEquivalentTo(ReportSnapshotCsv.FundingHeader);
        }
    }

    [Fact]
    public async Task RegenerateSnapshots_ExcludesDraftSubmissions()
    {
        var collectionId = await CreateOpenCollectionAsync(localAuthorityIds: [MainOrg.Id.Value, SecondOrg.Id.Value]);
        await SeedSubmittedSubmissionWithoutSnapshotAsync(collectionId, MainOrg);
        var draftId = await SeedDraftSubmissionAsync(collectionId, SecondOrg);

        AuthenticateAsAdmin();
        var response = await RegenerateAsync(collectionId);

        var result = await response.ToResponseModel<RegenerateReportSnapshotsResultDto>();
        result.Regenerated.Should().Be(1, "only the submitted organisation counts");
        result.Skipped.Should().BeEmpty("the draft is filtered out before the loop even runs, never recorded as skipped");

        var draft = await Context.Submissions.AsNoTracking().FirstAsync(s => s.Id == new SubmissionId(draftId));
        draft.ReportSnapshotJson.Should().BeNull("a draft must never gain a snapshot");
    }

    [Fact]
    public async Task RegenerateSnapshots_IsIdempotent()
    {
        var collectionId = await CreateOpenCollectionAsync();
        await SeedSubmittedSubmissionWithoutSnapshotAsync(collectionId, MainOrg);

        AuthenticateAsAdmin();
        var first = await RegenerateAsync(collectionId);
        var second = await RegenerateAsync(collectionId);

        (await first.ToResponseModel<RegenerateReportSnapshotsResultDto>()).Regenerated.Should().Be(1);
        (await second.ToResponseModel<RegenerateReportSnapshotsResultDto>()).Regenerated.Should().Be(1, "regenerating an already-current snapshot is a safe no-op-shaped success, not an error");
    }

    [Fact]
    public async Task RegenerateSnapshots_AsNonAdmin_Returns403()
    {
        var collectionId = await CreateOpenCollectionAsync();
        await SeedSubmittedSubmissionWithoutSnapshotAsync(collectionId, MainOrg);

        AuthenticateAsMainOrgLead();

        (await RegenerateAsync(collectionId)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RegenerateSnapshots_UnknownCollection_Returns404()
    {
        AuthenticateAsAdmin();

        (await RegenerateAsync(Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RegenerateSnapshots_MaturityAssessmentCollection_Returns404()
    {
        // Maturity assessments have no submission flow, so there is nothing to regenerate.
        var collectionId = await CreateMaturityAssessmentCollectionAsync(localAuthorityIds: [MainOrg.Id.Value]);

        AuthenticateAsAdmin();

        (await RegenerateAsync(collectionId)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // BLUM-7671: regeneration re-freezes a submitted report through GetActivityQuestionsAsync, so anything that
    // read carried-forward values into that response rewrote finalised history with them. Now that the carry-
    // forward is written at confirm time, a report contains exactly the rows the LA submitted — including the
    // absence of one they deleted. Under the old read-time overlay this test fails: regenerating Q2 would find
    // Q1's narrative for a code Q2 has no row for and bake it into the frozen snapshot.
    [Fact]
    public async Task RegenerateSnapshots_DoesNotBakeCarriedForwardValuesIntoAFinalisedReport()
    {
        AuthenticateAsMainOrgLead();
        var activity = await CreateRnaActivityAsync(status: "finished");

        var q1 = await CreateOpenQuarterCollectionAsync(1, "2026/27 Q1 quarterly report");
        await CreateSubmissionAsync(q1);
        await ConfirmActivitiesAsync(q1);
        await CompleteFinishedRnaReportAsync(q1, activity.Id, "https://example.org/rna", "Q1 progress.", "Q1 next steps.");
        await SubmitStartedReportAsync(q1);

        // Q2 inherits Q1's answers at confirm; the LA deletes the forward-look narrative and finalises without it.
        var q2 = await CreateOpenQuarterCollectionAsync(2, "2026/27 Q2 quarterly report");
        await CreateSubmissionAsync(q2);
        await ConfirmActivitiesAsync(q2);
        (await SaveAnswersAsync(q2, activity.Id, saveAsDraft: true, ("AKR_DELIVERY_NEXT_QUARTER", null)))
            .EnsureSuccessStatusCode();
        (await CompleteActivityAsync(q2, activity.Id)).EnsureSuccessStatusCode();
        await SubmitStartedReportAsync(q2);

        AuthenticateAsAdmin();
        var response = await RegenerateAsync(q2);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ToResponseModel<RegenerateReportSnapshotsResultDto>()).Regenerated.Should().Be(1);

        var saved = await Context.Submissions.AsNoTracking()
            .FirstAsync(s => s.CollectionId == new CollectionId(q2) && s.OrganisationId == MainOrg.Id);

        saved.ReportSnapshotJson.Should().NotBeNull();
        saved.ReportSnapshotJson.Should().NotContain(
            "Q1 next steps.",
            "regenerating a submitted report must not resurrect an answer the organisation deleted before submitting it");
        saved.ReportSnapshotJson.Should().Contain(
            "Q1 progress.",
            "the answers the organisation did carry and submit are part of the report and must survive regeneration");
    }

    [Fact]
    public async Task RegenerateReportSnapshotCommand_AgainstAnUnsubmittedSubmission_ReturnsSubmissionNotSubmittedException()
    {
        // The controller filters drafts out before this command is ever sent (see the "excludes drafts" test
        // above), so this exercises the handler's own guard directly rather than through HTTP.
        var collectionId = await CreateOpenCollectionAsync();
        await SeedDraftSubmissionAsync(collectionId, MainOrg);

        var result = await Sender.Send(new RegenerateReportSnapshotCommand(collectionId, MainOrg.Id.Value, "{}"));

        result.IsLeft.Should().BeTrue();
        result.IfLeft(error => error.Should().BeOfType<SubmissionNotSubmittedException>());
    }
}