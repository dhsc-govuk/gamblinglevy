using System.Net;
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
}