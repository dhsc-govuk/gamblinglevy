using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
using FluentAssertions;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.QuarterlyReports;

// GET quarterly-report/{collectionId}/context — the "Submit your quarterly report" intro context for ONE
// collection. Collection-scoped rather than resolved from "the organisation's current open window": two quarterly
// windows can be open at once, and the implicit pick opened the earliest-deadline one whatever the user clicked
// (BLUM-6950). A collection that does not exist, is a draft, is not quarterly, or is not assigned to the caller's
// organisation is a 404 (anti-probing). Reads do not require an open window — a closed report stays viewable, and
// the write commands are what enforce the window.
public class ReportContextTests(IntegrationTestWebFactory factory) : QuarterlyReportTestBase(factory)
{
    // Dates deserialise as DateTime — Newtonsoft cannot bind DateOnly, and reading them as strings is unsafe
    // because its date-parse handling rewrites date-like strings.
    private sealed record ContextResult(
        Guid CollectionId,
        string CollectionName,
        int Quarter,
        string FinancialYear,
        DateTime ReportingPeriodStart,
        DateTime ReportingPeriodEnd,
        DateTime SubmissionDeadline,
        string LocalAuthorityName,
        Guid? SubmissionId);

    private async Task<HttpResponseMessage> GetContextAsync(Guid collectionId) =>
        await Client.GetAsync($"quarterly-report/{collectionId}/context");

    [Fact]
    public async Task Context_WithOpenAssignedCollection_ReturnsQuarterPeriodDeadlineAndLaName()
    {
        var deadline = Today.AddDays(30);
        var collectionId = await CreateQuarterlyCollectionAsync("Q1 context", Today.AddDays(-1), deadline);
        AuthenticateAsMainOrgLead();

        var response = await GetContextAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "context body: {0}", await response.Content.ReadAsStringAsync());
        var dto = await response.ToResponseModel<ContextResult>();
        dto.CollectionId.Should().Be(collectionId);
        dto.CollectionName.Should().Be("Q1 context");
        dto.Quarter.Should().Be(1);
        dto.FinancialYear.Should().Be("2026/27");
        dto.ReportingPeriodStart.Date.Should().Be(new DateTime(2026, 4, 1));
        dto.ReportingPeriodEnd.Date.Should().Be(new DateTime(2026, 6, 30));
        dto.SubmissionDeadline.Date.Should().Be(deadline.ToDateTime(TimeOnly.MinValue));
        dto.LocalAuthorityName.Should().Be(MainOrg.Name);
        dto.SubmissionId.Should().BeNull();
    }

    // The BLUM-6950 regression. Two overlapping open windows: opening the later-deadline one must return ITS
    // quarter, period and deadline. The old collection-agnostic context always resolved the earliest deadline, so
    // clicking the Q3 row showed — and would have authored against — Q2.
    [Fact]
    public async Task Context_TwoOpenCollections_ReturnsTheRequestedCollectionNotTheEarliestDeadline()
    {
        var q2Deadline = Today.AddDays(20);
        var q3Deadline = Today.AddDays(40);
        var q2Id = await CreateQuarterlyCollectionAsync("Q2 earlier deadline", Today.AddDays(-1), q2Deadline, quarter: 2);
        var q3Id = await CreateQuarterlyCollectionAsync("Q3 later deadline", Today.AddDays(-1), q3Deadline, quarter: 3);
        AuthenticateAsMainOrgLead();

        var response = await GetContextAsync(q3Id);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "context body: {0}", await response.Content.ReadAsStringAsync());
        var dto = await response.ToResponseModel<ContextResult>();
        dto.CollectionId.Should().Be(q3Id);
        dto.CollectionId.Should().NotBe(q2Id);
        dto.Quarter.Should().Be(3);
        dto.ReportingPeriodStart.Date.Should().Be(new DateTime(2026, 10, 1));
        dto.ReportingPeriodEnd.Date.Should().Be(new DateTime(2026, 12, 31));
        dto.SubmissionDeadline.Date.Should().Be(q3Deadline.ToDateTime(TimeOnly.MinValue));
    }

    // The other side of the same scenario: the earlier window is still its own report, not shadowed by the later one.
    [Fact]
    public async Task Context_TwoOpenCollections_TheEarlierWindowStillReturnsItsOwnQuarter()
    {
        var q2Id = await CreateQuarterlyCollectionAsync("Q2 earlier deadline", Today.AddDays(-1), Today.AddDays(20), quarter: 2);
        await CreateQuarterlyCollectionAsync("Q3 later deadline", Today.AddDays(-1), Today.AddDays(40), quarter: 3);
        AuthenticateAsMainOrgLead();

        var response = await GetContextAsync(q2Id);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "context body: {0}", await response.Content.ReadAsStringAsync());
        var dto = await response.ToResponseModel<ContextResult>();
        dto.CollectionId.Should().Be(q2Id);
        dto.Quarter.Should().Be(2);
        dto.ReportingPeriodStart.Date.Should().Be(new DateTime(2026, 7, 1));
        dto.ReportingPeriodEnd.Date.Should().Be(new DateTime(2026, 9, 30));
    }

    [Fact]
    public async Task Context_AfterCreatingTheReport_ReturnsTheSubmissionId()
    {
        var collectionId = await CreateOpenCollectionAsync();
        AuthenticateAsMainOrgLead();
        var taskList = await CreateSubmissionAsync(collectionId);

        var response = await GetContextAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "context body: {0}", await response.Content.ReadAsStringAsync());
        var dto = await response.ToResponseModel<ContextResult>();
        dto.SubmissionId.Should().Be(taskList.SubmissionId);
    }

    [Fact]
    public async Task Context_OrganisationNotAssigned_ReturnsNotFound()
    {
        var collectionId = await CreateOpenCollectionAsync();
        AuthenticateAsSecondOrgLead();

        var response = await GetContextAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Context_UnknownCollection_ReturnsNotFound()
    {
        await CreateOpenCollectionAsync();
        AuthenticateAsMainOrgLead();

        var response = await GetContextAsync(Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // A draft can already carry a quarterly Type while its quarter, period and deadline are unanswered, so it must
    // be rejected before the context's null-forgiving reads (which would otherwise throw).
    [Fact]
    public async Task Context_DraftCollection_ReturnsNotFound()
    {
        var collectionId = await CreateDraftQuarterlyCollectionAsync("Draft quarterly collection");
        AuthenticateAsMainOrgLead();

        var response = await GetContextAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Context_MaturityAssessmentCollection_ReturnsNotFound()
    {
        var collectionId = await CreateMaturityAssessmentCollectionAsync();
        AuthenticateAsMainOrgLead();

        var response = await GetContextAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Context_AllLasCollection_VisibleToOrgWithoutExplicitAssignment()
    {
        // Published with "submitted by all LAs" = Yes and NO collection_local_authorities rows. MainOrg has no
        // explicit assignment, so it can only surface via the IsSubmittedByAllLas flag.
        var collectionId = await CreateOpenAllLasCollectionAsync("All-LAs Q1 context");
        AuthenticateAsMainOrgLead();

        var response = await GetContextAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "context body: {0}", await response.Content.ReadAsStringAsync());
        var dto = await response.ToResponseModel<ContextResult>();
        dto.CollectionId.Should().Be(collectionId);
        dto.LocalAuthorityName.Should().Be(MainOrg.Name);
        dto.SubmissionId.Should().BeNull();
    }

    [Fact]
    public async Task Context_AllLasCollection_VcseOrg_ReturnsNotFound()
    {
        // "Submitted by all LAs" targets Local Authorities only — a VCSE-type org must not see it, even though
        // the flag matches every org id before the type gate.
        var collectionId = await CreateOpenAllLasCollectionAsync("All-LAs Q1 vcse");
        AuthenticateAsVcseOrgLead();

        var response = await GetContextAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Reads are view-only, so a window that has not opened yet is still readable — the LA landing never links to
    // it, and CreateSubmission is what refuses to start the report.
    [Fact]
    public async Task Context_ScheduledCollection_IsStillReadable()
    {
        var collectionId = await CreateQuarterlyCollectionAsync("Scheduled collection", Today.AddDays(5), Today.AddDays(30));
        AuthenticateAsMainOrgLead();

        var response = await GetContextAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "context body: {0}", await response.Content.ReadAsStringAsync());
        var dto = await response.ToResponseModel<ContextResult>();
        dto.CollectionId.Should().Be(collectionId);
        dto.SubmissionId.Should().BeNull();
    }

    // A closed window keeps its report viewable (the confirmation page and its download still resolve after the
    // deadline passes). Writes against it still fail — that is enforced by the commands, not this read.
    [Fact]
    public async Task Context_ManuallyClosedCollection_IsStillReadable()
    {
        var collectionId = await CreateOpenCollectionAsync();
        AuthenticateAsMainOrgLead();
        var taskList = await CreateSubmissionAsync(collectionId);
        await CloseCollectionAsync(collectionId);
        AuthenticateAsMainOrgLead();

        var response = await GetContextAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "context body: {0}", await response.Content.ReadAsStringAsync());
        var dto = await response.ToResponseModel<ContextResult>();
        dto.CollectionId.Should().Be(collectionId);
        dto.SubmissionId.Should().Be(taskList.SubmissionId);
    }

    // A partial draft: quarterly Type chosen and the LAs picked, but the quarter, window and deadline unanswered.
    private sealed record CollectionResult(Guid Id);

    private async Task<Guid> CreateDraftQuarterlyCollectionAsync(string name)
    {
        AuthenticateAsAdmin();

        var request = new CreateCollectionRequestDto(
            Name: name,
            Purpose: null,
            Type: "quarterly_report",
            FinancialYear: null,
            Quarter: null,
            WindowSameAsReportingPeriod: null,
            OpenDate: null,
            Deadline: null,
            IsSubmittedByAllLas: null,
            SaveAsDraft: true,
            LocalAuthorityIds: [MainOrg.Id.Value],
            FormModuleIds: null,
            Reminders: null);

        var response = await Client.PostAsJsonAsync("data-collections", request);
        response.IsSuccessStatusCode.Should().BeTrue(
            "creating draft collection \"{0}\" failed: {1}", name, await response.Content.ReadAsStringAsync());
        var dto = await response.ToResponseModel<CollectionResult>();
        return dto.Id;
    }
}