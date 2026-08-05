using System.Net;
using Api.Dtos;
using Domain.Collections;
using Domain.DataCollectionForms;
using Domain.Organisations;
using Domain.Submissions;
using Domain.Users;
using Domain.ValueObjects;
using FluentAssertions;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.QuarterlyReports;

// DHSC submission visibility (BLUM-6095 / BLUM-6445): the per-window organisation status list and a single
// organisation's submission detail, both exposed on the admin-only DataCollectionsController and backed by the
// derived per-org status (Not started / In progress / Completed / Overdue).
public class DhscSubmissionStatusTests(IntegrationTestWebFactory factory) : QuarterlyReportTestBase(factory)
{
    private Task<HttpResponseMessage> GetSubmissionsAsync(Guid collectionId) =>
        Client.GetAsync($"data-collections/{collectionId}/submissions");

    private Task<HttpResponseMessage> GetSubmissionDetailAsync(Guid collectionId, Guid organisationId) =>
        Client.GetAsync($"data-collections/{collectionId}/submissions/{organisationId}");

    // Seeds a finalised (SubmittedAt set) submission directly for (collection, org — MainOrg by default), submitted by
    // a real user row — enough to exercise the "Completed" status and submitter name/email resolution without driving
    // the whole multi-section submit flow.
    private async Task<User> SeedSubmittedSubmissionAsync(Guid collectionId, Organisation? org = null)
    {
        var submitter = User.New(
            UserId.New(),
            new Name("Julie", "Smith"),
            "julie.smith@example.gov.uk",
            new SubId(Guid.NewGuid()),
            UserStatus.Active,
            UserRole.Lead);
        await Context.Users.AddAsync(submitter);

        var submission = Submission.Start(
            SubmissionId.New(),
            new CollectionId(collectionId),
            (org ?? MainOrg).Id,
            Array.Empty<(FormModuleId ModuleId, string ModuleCode)>(),
            submitter.Id);
        submission.Submit("202701-0001", submitter.Id);
        await Context.Submissions.AddAsync(submission);

        await SaveChangesAsync();
        return submitter;
    }

    [Fact]
    public async Task GetSubmissions_ExplicitCollection_ListsAssignedOrgsWithDerivedStatuses()
    {
        var collectionId = await CreateOpenCollectionAsync(localAuthorityIds: [MainOrg.Id.Value, SecondOrg.Id.Value]);

        AuthenticateAsAdmin();
        var before = await (await GetSubmissionsAsync(collectionId)).ToResponseModel<CollectionSubmissionSummaryDto>();

        before.CollectionStatus.Should().Be("open");
        before.Organisations.Select(o => o.OrganisationId)
            .Should().BeEquivalentTo([MainOrg.Id.Value, SecondOrg.Id.Value]);
        before.Organisations.Should().OnlyContain(o => o.Status == "not_started");

        // A started-but-unsubmitted report flips that org to In progress; the others stay Not started.
        AuthenticateAsMainOrgLead();
        await CreateSubmissionAsync(collectionId);

        AuthenticateAsAdmin();
        var after = await (await GetSubmissionsAsync(collectionId)).ToResponseModel<CollectionSubmissionSummaryDto>();

        after.Organisations.Single(o => o.OrganisationId == MainOrg.Id.Value).Status.Should().Be("in_progress");
        after.Organisations.Single(o => o.OrganisationId == SecondOrg.Id.Value).Status.Should().Be("not_started");
        after.Organisations.Single(o => o.OrganisationId == MainOrg.Id.Value).SubmittedAt.Should().BeNull();
    }

    [Fact]
    public async Task GetSubmissions_AllLasCollection_IncludesLocalAuthoritiesAndExcludesVcse()
    {
        var collectionId = await CreateOpenAllLasCollectionAsync();

        AuthenticateAsAdmin();
        var summary = await (await GetSubmissionsAsync(collectionId)).ToResponseModel<CollectionSubmissionSummaryDto>();

        var orgIds = summary.Organisations.Select(o => o.OrganisationId).ToList();
        orgIds.Should().Contain(MainOrg.Id.Value);
        orgIds.Should().Contain(SecondOrg.Id.Value);
        orgIds.Should().NotContain(VcseOrg.Id.Value);
        summary.Organisations.Should().OnlyContain(o => o.Status == "not_started");
    }

    [Fact]
    public async Task GetSubmissions_PastDeadlineWithNoSubmission_ShowsOverdue()
    {
        var collectionId = await CreateQuarterlyCollectionAsync(
            "2026 Q1 overdue report",
            openDate: Today.AddDays(-30),
            deadline: Today.AddDays(-1),
            localAuthorityIds: [MainOrg.Id.Value]);

        AuthenticateAsAdmin();
        var summary = await (await GetSubmissionsAsync(collectionId)).ToResponseModel<CollectionSubmissionSummaryDto>();

        summary.CollectionStatus.Should().Be("closed");
        summary.Organisations.Single(o => o.OrganisationId == MainOrg.Id.Value).Status.Should().Be("overdue");
    }

    [Fact]
    public async Task GetSubmissionDetail_NotStarted_ReturnsEmptyState()
    {
        var collectionId = await CreateOpenCollectionAsync();

        AuthenticateAsAdmin();
        var detail = await (await GetSubmissionDetailAsync(collectionId, MainOrg.Id.Value)).ToResponseModel<OrgSubmissionDetailDto>();

        detail.OrganisationId.Should().Be(MainOrg.Id.Value);
        detail.HasSubmission.Should().BeFalse();
        detail.Status.Should().Be("not_started");
        detail.SubmittedByName.Should().BeNull();
        detail.SubmittedByEmail.Should().BeNull();
        detail.SubmittedAt.Should().BeNull();
    }

    [Fact]
    public async Task GetSubmissionDetail_Submitted_ReturnsSubmitterAndCompletedStatus()
    {
        var collectionId = await CreateOpenCollectionAsync();
        await SeedSubmittedSubmissionAsync(collectionId);

        AuthenticateAsAdmin();
        var detail = await (await GetSubmissionDetailAsync(collectionId, MainOrg.Id.Value)).ToResponseModel<OrgSubmissionDetailDto>();

        detail.HasSubmission.Should().BeTrue();
        detail.Status.Should().Be("completed");
        detail.SubmittedByName.Should().Be("Julie Smith");
        detail.SubmittedByEmail.Should().Be("julie.smith@example.gov.uk");
        detail.SubmittedAt.Should().NotBeNull();
        detail.ReferenceNumber.Should().Be("202701-0001");
    }

    // BLUM-7216: a started-but-unsubmitted report — "Incomplete" in the Organisations list — must open and show the
    // partial data it has. The status list has always covered this shape; the detail endpoint never did, and every
    // other "has a submission" fixture here is finalised, so nothing exercised hasSubmission && !IsSubmitted.
    [Fact]
    public async Task GetSubmissionDetail_InProgress_ReturnsPartialDetail()
    {
        var collectionId = await CreateOpenCollectionAsync();

        AuthenticateAsMainOrgLead();
        await CreateSubmissionAsync(collectionId);

        AuthenticateAsAdmin();
        var response = await GetSubmissionDetailAsync(collectionId, MainOrg.Id.Value);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await response.ToResponseModel<OrgSubmissionDetailDto>();

        detail.OrganisationId.Should().Be(MainOrg.Id.Value);
        detail.HasSubmission.Should().BeTrue();
        detail.Status.Should().Be("in_progress");
        detail.SubmittedAt.Should().BeNull();
        detail.ReferenceNumber.Should().BeNull();
        detail.SubmittedByName.Should().BeNull();
        detail.SubmittedByEmail.Should().BeNull();
    }

    // The same case, but with a fully authored (and still unsubmitted) report behind it: sections, activity answers,
    // spend entries and feedback answers all present. This is the shape a real LA leaves behind after working through
    // the task list without pressing Submit, and the one the seeded fixtures above never produce.
    [Fact]
    public async Task GetSubmissionDetail_InProgressWithAuthoredSections_ReturnsPartialDetail()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        var activity = await CreateRnaActivityAsync();

        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        await CompleteActivityReportAsync(collectionId, activity.Id);
        await CompleteFundingSpendSectionAsync(collectionId);
        await CompleteStrategicFeedbackSectionAsync(collectionId);

        // Deliberately NOT submitted — every section is Completed but SubmittedAt is still null.
        AuthenticateAsAdmin();
        var response = await GetSubmissionDetailAsync(collectionId, MainOrg.Id.Value);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await response.ToResponseModel<OrgSubmissionDetailDto>();

        detail.HasSubmission.Should().BeTrue();
        detail.Status.Should().Be("in_progress");
        detail.SubmittedAt.Should().BeNull();
    }

    // BLUM-7079: the detail must carry the REPORTING PERIOD (what the report is about, derived from financial year +
    // quarter by ReportingPeriod.Derive) as well as the collection window (when LAs may submit). Deliberately
    // published with WindowSameAsReportingPeriod = false and a window that sits entirely AFTER the reported quarter,
    // so the two ranges cannot coincide — a regression that re-wired the period back to the window dates fails here.
    // Fixed dates rather than Today-relative ones: the point of the test is that the two ranges differ.
    [Fact]
    public async Task GetSubmissionDetail_ReturnsReportingPeriodDistinctFromCollectionWindow()
    {
        var collectionId = await CreateQuarterlyCollectionAsync(
            "2026/27 Q2 quarterly report",
            openDate: new DateOnly(2026, 10, 1),
            deadline: new DateOnly(2026, 11, 15),
            quarter: 2,
            financialYear: "2026/27");

        AuthenticateAsAdmin();
        var detail = await (await GetSubmissionDetailAsync(collectionId, MainOrg.Id.Value)).ToResponseModel<OrgSubmissionDetailDto>();

        detail.Quarter.Should().Be(2);
        detail.ReportingPeriodStart.Should().Be(new DateOnly(2026, 7, 1));
        detail.ReportingPeriodEnd.Should().Be(new DateOnly(2026, 9, 30));

        detail.StartDate.Should().Be(new DateOnly(2026, 10, 1));
        detail.Deadline.Should().Be(new DateOnly(2026, 11, 15));
    }

    // The window-level list carries the same two ranges, because the Organisations page names the consolidated export
    // by the reporting period while listing the window as its "Start date"/"Deadline" rows. Before BLUM-7079 the page
    // had only the window and guessed the quarter from the collection name, so it contradicted the detail page.
    [Fact]
    public async Task GetSubmissions_ReturnsReportingPeriodDistinctFromCollectionWindow()
    {
        var collectionId = await CreateQuarterlyCollectionAsync(
            "2026/27 Q2 quarterly report",
            openDate: new DateOnly(2026, 10, 1),
            deadline: new DateOnly(2026, 11, 15),
            quarter: 2,
            financialYear: "2026/27");

        AuthenticateAsAdmin();
        var summary = await (await GetSubmissionsAsync(collectionId)).ToResponseModel<CollectionSubmissionSummaryDto>();

        summary.Quarter.Should().Be(2);
        summary.ReportingPeriodStart.Should().Be(new DateOnly(2026, 7, 1));
        summary.ReportingPeriodEnd.Should().Be(new DateOnly(2026, 9, 30));

        summary.StartDate.Should().Be(new DateOnly(2026, 10, 1));
        summary.Deadline.Should().Be(new DateOnly(2026, 11, 15));
    }

    [Fact]
    public async Task GetSubmissions_UnknownCollection_Returns404()
    {
        AuthenticateAsAdmin();
        var response = await GetSubmissionsAsync(Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetSubmissionDetail_OrganisationNotAssigned_Returns404()
    {
        // Collection assigned to MainOrg only.
        var collectionId = await CreateOpenCollectionAsync();

        AuthenticateAsAdmin();
        var secondOrgResponse = await GetSubmissionDetailAsync(collectionId, SecondOrg.Id.Value);
        var vcseResponse = await GetSubmissionDetailAsync(collectionId, VcseOrg.Id.Value);

        secondOrgResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        vcseResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetSubmissions_OrgWithSubmissionNotAssigned_IsStillListed()
    {
        // Collection assigned to MainOrg only; SecondOrg has a finalised submission but is NOT assigned (e.g. it was
        // de-assigned after submitting). Its data must still surface in the status list, not silently disappear.
        var collectionId = await CreateOpenCollectionAsync(localAuthorityIds: [MainOrg.Id.Value]);
        await SeedSubmittedSubmissionAsync(collectionId, SecondOrg);

        AuthenticateAsAdmin();
        var summary = await (await GetSubmissionsAsync(collectionId)).ToResponseModel<CollectionSubmissionSummaryDto>();

        var orgIds = summary.Organisations.Select(o => o.OrganisationId).ToList();
        orgIds.Should().Contain(MainOrg.Id.Value);
        orgIds.Should().Contain(SecondOrg.Id.Value);
        orgIds.Should().OnlyHaveUniqueItems();
        summary.Organisations.Single(o => o.OrganisationId == SecondOrg.Id.Value).Status.Should().Be("completed");
        summary.Organisations.Single(o => o.OrganisationId == MainOrg.Id.Value).Status.Should().Be("not_started");
    }

    [Fact]
    public async Task GetSubmissionDetail_OrgWithSubmissionNotAssigned_ReturnsDetail()
    {
        // A now-unassigned org that has a submission must have a viewable detail page (it appears in the list) — unlike
        // an unassigned org with no submission, which still 404s.
        var collectionId = await CreateOpenCollectionAsync(localAuthorityIds: [MainOrg.Id.Value]);
        await SeedSubmittedSubmissionAsync(collectionId, SecondOrg);

        AuthenticateAsAdmin();
        var detail = await (await GetSubmissionDetailAsync(collectionId, SecondOrg.Id.Value)).ToResponseModel<OrgSubmissionDetailDto>();

        detail.OrganisationId.Should().Be(SecondOrg.Id.Value);
        detail.HasSubmission.Should().BeTrue();
        detail.Status.Should().Be("completed");
    }

    [Fact]
    public async Task GetSubmissions_MaturityAssessmentCollection_Returns404()
    {
        // Maturity assessments have no submission flow; the Reports status surface is quarterly-only. Past its deadline,
        // so the old (ungated) behaviour would have wrongly shown every assigned org as Overdue.
        var collectionId = await CreateMaturityAssessmentCollectionAsync(
            openDate: Today.AddDays(-30),
            deadline: Today.AddDays(-1),
            localAuthorityIds: [MainOrg.Id.Value]);

        AuthenticateAsAdmin();
        (await GetSubmissionsAsync(collectionId)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetSubmissionDetail_MaturityAssessmentCollection_Returns404()
    {
        var collectionId = await CreateMaturityAssessmentCollectionAsync(localAuthorityIds: [MainOrg.Id.Value]);

        AuthenticateAsAdmin();
        (await GetSubmissionDetailAsync(collectionId, MainOrg.Id.Value)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetSubmissions_AsNonAdmin_Returns403()
    {
        var collectionId = await CreateOpenCollectionAsync();

        AuthenticateAsMainOrgLead();
        var listResponse = await GetSubmissionsAsync(collectionId);
        var detailResponse = await GetSubmissionDetailAsync(collectionId, MainOrg.Id.Value);

        listResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        detailResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}