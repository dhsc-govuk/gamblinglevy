using System.Net;
using System.Net.Http.Json;
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

// LA submission history + detail (BLUM-6097 / BLUM-6098): the org-scoped "my collections" list and "my submission
// detail" reads on the LA-facing QuarterlyReportController. The org is taken from the caller's permission (never the
// route), so an LA only ever sees its own submissions. Mirrors the DHSC DhscSubmissionStatusTests, re-scoped.
public class LaSubmissionHistoryTests(IntegrationTestWebFactory factory) : QuarterlyReportTestBase(factory)
{
    private Task<HttpResponseMessage> GetMyCollectionsAsync() =>
        Client.GetAsync("quarterly-report/collections");

    private Task<HttpResponseMessage> GetMyDetailAsync(Guid collectionId) =>
        Client.GetAsync($"quarterly-report/submissions/{collectionId}");

    // Seeds a finalised (SubmittedAt set) submission directly for (collection, org — MainOrg by default), submitted by
    // a real user row — enough to exercise the "Completed" status and submitter name/email resolution without driving
    // the whole multi-section submit flow (the DhscSubmissionStatusTests precedent).
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

    // --- "My collections" list (BLUM-6097) ---

    [Fact]
    public async Task GetMyCollections_ListsOnlyOwnOrgQuarterlyCollections()
    {
        // One collection assigned to MainOrg, another to SecondOrg only.
        var mainCollectionId = await CreateOpenCollectionAsync("2026 Q1 main report", localAuthorityIds: [MainOrg.Id.Value]);
        await CreateOpenCollectionAsync("2026 Q1 second-org report", localAuthorityIds: [SecondOrg.Id.Value]);

        AuthenticateAsMainOrgLead();
        var collections = await (await GetMyCollectionsAsync()).ToResponseModel<List<MyCollectionSummaryDto>>();

        collections.Should().ContainSingle();
        collections.Single().CollectionId.Should().Be(mainCollectionId);
        collections.Single().Type.Should().Be("quarterly_report");
        collections.Single().CollectionStatus.Should().Be("open");
        collections.Single().SubmissionStatus.Should().Be("not_started");
        collections.Single().SubmittedAt.Should().BeNull();
    }

    [Fact]
    public async Task GetMyCollections_SubmittedReport_IsCompletedWithSubmittedAt()
    {
        var collectionId = await CreateOpenCollectionAsync(localAuthorityIds: [MainOrg.Id.Value]);
        await SeedSubmittedSubmissionAsync(collectionId);

        AuthenticateAsMainOrgLead();
        var collections = await (await GetMyCollectionsAsync()).ToResponseModel<List<MyCollectionSummaryDto>>();

        var row = collections.Single(c => c.CollectionId == collectionId);
        row.SubmissionStatus.Should().Be("completed");
        row.SubmittedAt.Should().NotBeNull();
    }

    // The BLUM-6951 regression, at the list level: with two open windows, the submitted one must not drag the other
    // into "Completed" with it. Each row's status is derived from that collection's own submission row.
    [Fact]
    public async Task GetMyCollections_TwoOpenCollections_OnlyTheSubmittedOneIsCompleted()
    {
        var submittedId = await CreateOpenQuarterCollectionAsync(1, "2026 Q1 report");
        var openId = await CreateOpenQuarterCollectionAsync(2, "2026 Q2 report");
        await SeedSubmittedSubmissionAsync(submittedId);

        AuthenticateAsMainOrgLead();
        var collections = await (await GetMyCollectionsAsync()).ToResponseModel<List<MyCollectionSummaryDto>>();

        collections.Single(c => c.CollectionId == submittedId).SubmissionStatus.Should().Be("completed");

        var stillOpen = collections.Single(c => c.CollectionId == openId);
        stillOpen.CollectionStatus.Should().Be("open");
        stillOpen.SubmissionStatus.Should().Be("not_started");
        stillOpen.SubmittedAt.Should().BeNull();
    }

    // BLUM-7077: a window whose deadline passed with nothing submitted must still be listed, carrying the derived
    // "overdue" status. It is the only signal the LA has that it missed a reporting deadline — the frontend had been
    // filtering these rows off both the Reports landing (open-only) and the report history (submitted-only), which
    // left a missed deadline invisible everywhere in the service.
    [Fact]
    public async Task GetMyCollections_PastDeadlineWithoutSubmission_IsOverdue()
    {
        var collectionId = await CreateQuarterlyCollectionAsync(
            "2026 Q1 overdue report",
            openDate: Today.AddDays(-30),
            deadline: Today.AddDays(-1),
            localAuthorityIds: [MainOrg.Id.Value]);

        AuthenticateAsMainOrgLead();
        var collections = await (await GetMyCollectionsAsync()).ToResponseModel<List<MyCollectionSummaryDto>>();

        var row = collections.Single(c => c.CollectionId == collectionId);
        row.CollectionStatus.Should().Be("closed");
        row.SubmissionStatus.Should().Be("overdue");
        row.SubmittedAt.Should().BeNull();
    }

    [Fact]
    public async Task GetMyCollections_ExcludesMaturityAssessment()
    {
        var quarterlyId = await CreateOpenCollectionAsync(localAuthorityIds: [MainOrg.Id.Value]);
        await CreateMaturityAssessmentCollectionAsync(localAuthorityIds: [MainOrg.Id.Value]);

        AuthenticateAsMainOrgLead();
        var collections = await (await GetMyCollectionsAsync()).ToResponseModel<List<MyCollectionSummaryDto>>();

        collections.Should().ContainSingle();
        collections.Single().CollectionId.Should().Be(quarterlyId);
    }

    [Fact]
    public async Task GetMyCollections_SubmittedButUnassigned_IsStillListed()
    {
        // Collection assigned to SecondOrg only; MainOrg has a finalised submission (e.g. it was de-assigned after
        // submitting). The submitted window must still surface in MainOrg's history, not silently disappear.
        var collectionId = await CreateOpenCollectionAsync(localAuthorityIds: [SecondOrg.Id.Value]);
        await SeedSubmittedSubmissionAsync(collectionId, MainOrg);

        AuthenticateAsMainOrgLead();
        var collections = await (await GetMyCollectionsAsync()).ToResponseModel<List<MyCollectionSummaryDto>>();

        collections.Single(c => c.CollectionId == collectionId).SubmissionStatus.Should().Be("completed");
    }

    [Fact]
    public async Task GetMyCollections_AsAdmin_Returns403()
    {
        await CreateOpenCollectionAsync(localAuthorityIds: [MainOrg.Id.Value]);

        // Default identity is platform admin — not an organisation member, so the LA surface is forbidden.
        AuthenticateAsAdmin();
        var response = await GetMyCollectionsAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // --- "My submission detail" (BLUM-6098) ---

    [Fact]
    public async Task GetMyDetail_Submitted_ReturnsSubmitterAndCompletedStatus()
    {
        var collectionId = await CreateOpenCollectionAsync(localAuthorityIds: [MainOrg.Id.Value]);
        await SeedSubmittedSubmissionAsync(collectionId);

        AuthenticateAsMainOrgLead();
        var detail = await (await GetMyDetailAsync(collectionId)).ToResponseModel<MySubmissionDetailDto>();

        detail.CollectionId.Should().Be(collectionId);
        detail.HasSubmission.Should().BeTrue();
        detail.Status.Should().Be("completed");
        detail.LocalAuthorityName.Should().Be(MainOrg.Name);
        detail.SubmittedByName.Should().Be("Julie Smith");
        detail.SubmittedByEmail.Should().Be("julie.smith@example.gov.uk");
        detail.SubmittedAt.Should().NotBeNull();
        detail.Quarter.Should().Be(1);
    }

    [Fact]
    public async Task GetMyDetail_NotStarted_ReturnsEmptyState()
    {
        var collectionId = await CreateOpenCollectionAsync(localAuthorityIds: [MainOrg.Id.Value]);

        AuthenticateAsMainOrgLead();
        var detail = await (await GetMyDetailAsync(collectionId)).ToResponseModel<MySubmissionDetailDto>();

        detail.HasSubmission.Should().BeFalse();
        detail.Status.Should().Be("not_started");
        detail.SubmittedByName.Should().BeNull();
        detail.SubmittedByEmail.Should().BeNull();
        detail.SubmittedAt.Should().BeNull();
    }

    [Fact]
    public async Task GetMyDetail_SubmittedButUnassigned_ReturnsDetail()
    {
        // A now-unassigned org that has a submission must still have a viewable detail (the anti-probing guard 404s
        // only when there is neither an assignment nor a submission).
        var collectionId = await CreateOpenCollectionAsync(localAuthorityIds: [SecondOrg.Id.Value]);
        await SeedSubmittedSubmissionAsync(collectionId, MainOrg);

        AuthenticateAsMainOrgLead();
        var detail = await (await GetMyDetailAsync(collectionId)).ToResponseModel<MySubmissionDetailDto>();

        detail.HasSubmission.Should().BeTrue();
        detail.Status.Should().Be("completed");
    }

    [Fact]
    public async Task GetMyDetail_OtherOrgCollection_Returns404()
    {
        // Collection assigned to SecondOrg only — MainOrg is neither assigned nor has a submission for it.
        var collectionId = await CreateOpenCollectionAsync(localAuthorityIds: [SecondOrg.Id.Value]);

        AuthenticateAsMainOrgLead();
        var response = await GetMyDetailAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetMyDetail_UnknownCollection_Returns404()
    {
        AuthenticateAsMainOrgLead();
        var response = await GetMyDetailAsync(Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetMyDetail_MaturityAssessment_Returns404()
    {
        var collectionId = await CreateMaturityAssessmentCollectionAsync(localAuthorityIds: [MainOrg.Id.Value]);

        AuthenticateAsMainOrgLead();
        var response = await GetMyDetailAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetMyDetail_DraftCollection_Returns404()
    {
        // A draft the org is assigned to must not leak its setup metadata before publication.
        var collectionId = await CreateDraftQuarterlyCollectionAsync();

        AuthenticateAsMainOrgLead();
        var response = await GetMyDetailAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private sealed record CreatedCollection(Guid Id);

    // Publishes a draft (unpublished) quarterly collection assigned to MainOrg through the admin API.
    private async Task<Guid> CreateDraftQuarterlyCollectionAsync()
    {
        AuthenticateAsAdmin();
        var request = new CreateCollectionRequestDto(
            Name: "2026 Q1 draft report",
            Purpose: "Draft tests",
            Type: "quarterly_report",
            FinancialYear: "2026/27",
            Quarter: 1,
            WindowSameAsReportingPeriod: false,
            OpenDate: Today.AddDays(-1),
            Deadline: Today.AddDays(60),
            IsSubmittedByAllLas: false,
            SaveAsDraft: true,
            LocalAuthorityIds: [MainOrg.Id.Value],
            FormModuleIds: null,
            Reminders: null);

        var response = await Client.PostAsJsonAsync("data-collections", request);
        response.IsSuccessStatusCode.Should().BeTrue("creating draft collection failed: {0}", await response.Content.ReadAsStringAsync());
        var dto = await response.ToResponseModel<CreatedCollection>();
        return dto.Id;
    }
}