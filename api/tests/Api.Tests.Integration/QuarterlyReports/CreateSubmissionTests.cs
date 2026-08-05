using System.Net;
using Domain.Collections;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.QuarterlyReports;

// POST quarterly-report/{collectionId}/submission — the "Create report" step.
public class CreateSubmissionTests(IntegrationTestWebFactory factory) : QuarterlyReportTestBase(factory)
{
    private sealed record ErrorEnvelope(string Error, string Message);

    private Task<HttpResponseMessage> PostSubmissionAsync(Guid collectionId) =>
        Client.PostAsync($"quarterly-report/{collectionId}/submission", null);

    [Fact]
    public async Task CreateSubmission_CreatesOneUnavailableSectionPerAssignedModule()
    {
        var collectionId = await CreateOpenCollectionAsync();
        AuthenticateAsMainOrgLead();

        var response = await PostSubmissionAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var taskList = await response.ToResponseModel<Api.Dtos.SubmissionTaskListDto>();
        taskList.ActivitiesConfirmed.Should().BeFalse();
        taskList.ActivitiesReviewRequired.Should().BeFalse();
        taskList.Sections.Select(s => s.ModuleCode).Should().BeEquivalentTo(
            KeyResultsModuleCode, FundingModuleCode, FeedbackModuleCode);
        taskList.Sections.Should().OnlyContain(s => s.Status == "not_started" && !s.IsAvailable && !s.ReviewRequired);

        var persisted = await Context.Submissions.AsNoTracking().Include(s => s.Sections).SingleAsync();
        persisted.Id.Value.Should().Be(taskList.SubmissionId);
        persisted.CollectionId.Should().Be(new CollectionId(collectionId));
        persisted.OrganisationId.Should().Be(MainOrg.Id);
        persisted.AreActivitiesConfirmed.Should().BeFalse();
        persisted.Sections.Should().HaveCount(3);
    }

    [Fact]
    public async Task CreateSubmission_Twice_ReturnsConflict()
    {
        var collectionId = await CreateOpenCollectionAsync();
        AuthenticateAsMainOrgLead();
        await CreateSubmissionAsync(collectionId);

        var response = await PostSubmissionAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Context.Submissions.AsNoTracking().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CreateSubmission_AllLasCollection_SucceedsForOrgWithoutExplicitAssignment()
    {
        // "Submitted by all LAs" collection has no assignment row for MainOrg — eligibility must honour the flag.
        var collectionId = await CreateOpenAllLasCollectionAsync();
        AuthenticateAsMainOrgLead();

        var response = await PostSubmissionAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "create body: {0}", await response.Content.ReadAsStringAsync());
        var taskList = await response.ToResponseModel<Api.Dtos.SubmissionTaskListDto>();
        var persisted = await Context.Submissions.AsNoTracking().SingleAsync();
        persisted.Id.Value.Should().Be(taskList.SubmissionId);
        persisted.CollectionId.Should().Be(new CollectionId(collectionId));
        persisted.OrganisationId.Should().Be(MainOrg.Id);
    }

    [Fact]
    public async Task CreateSubmission_AllLasCollection_VcseOrg_ReturnsNotFound()
    {
        // The all-LAs flag matches any org id; eligibility must additionally require OrgType == LA, so a VCSE org
        // cannot create a submission for an all-LAs collection.
        var collectionId = await CreateOpenAllLasCollectionAsync();
        AuthenticateAsVcseOrgLead();

        var response = await PostSubmissionAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Context.Submissions.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task CreateSubmission_CollectionNotAssignedToCallersOrg_ReturnsNotFound()
    {
        var collectionId = await CreateOpenCollectionAsync();
        AuthenticateAsSecondOrgLead();

        var response = await PostSubmissionAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Context.Submissions.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task CreateSubmission_UnknownCollection_ReturnsNotFound()
    {
        AuthenticateAsMainOrgLead();

        var response = await PostSubmissionAsync(Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateSubmission_ClosedCollection_Returns422ReportNotOpen()
    {
        var collectionId = await CreateQuarterlyCollectionAsync("Closed window", Today.AddDays(-10), Today.AddDays(-1));
        AuthenticateAsMainOrgLead();

        var response = await PostSubmissionAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var envelope = await response.ToResponseModel<ErrorEnvelope>();
        envelope.Error.Should().Be("REPORT_NOT_OPEN");
        (await Context.Submissions.AsNoTracking().AnyAsync()).Should().BeFalse();
    }
}