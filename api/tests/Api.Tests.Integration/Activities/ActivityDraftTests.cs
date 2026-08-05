using System.Net.Http.Json;
using Api.Dtos;
using Domain.AuditLogs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.Activities;

// Draft activity records ("Save and come back later"): incomplete setup, completion via update, review exclusion.
public class ActivityDraftTests(IntegrationTestWebFactory factory) : ActivityTestBase(factory)
{
    [Fact]
    public async Task ShouldSaveDraft_WithPartialFields()
    {
        AuthenticateAsMainOrgLead();

        var response = await Client.PostAsJsonAsync(
            "activities",
            new CreateActivityRequestDto("Incomplete idea", null, null, null, null, null, null, SaveAsDraft: true));

        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<ActivityDto>();
        dto.IsDraft.Should().BeTrue();
        dto.Status.Should().BeNull();
    }

    [Fact]
    public async Task ShouldCompleteDraft_ViaUpdate_AndAuditAsCompleted()
    {
        AuthenticateAsMainOrgLead();
        var draft = await CreateDraftActivityAsync();
        var (categoryId, taskId) = await GetCategoryAndTypeAsync();

        var response = await Client.PutAsJsonAsync(
            $"activities/{draft.Id}",
            new UpdateActivityRequestDto("Now complete", categoryId, taskId, null, "planned", null, null, false, DefaultExpectedResults));

        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<ActivityDto>();
        dto.IsDraft.Should().BeFalse();
        dto.Status.Should().Be("planned");

        var audit = await Context.AuditLogs.AsNoTracking()
            .Where(x => x.TargetType == AuditTargetType.Activity && x.TargetId == draft.Id)
            .ToListAsync();
        audit.Should().Contain(x => x.Action == AuditAction.ActivityCompleted);
    }

    [Fact]
    public async Task ShouldExcludeDraft_FromReview()
    {
        AuthenticateAsMainOrgLead();
        await CreateDraftActivityAsync();
        await CreateActiveActivityAsync("Active one");

        var response = await Client.GetAsync("activities/review");
        response.EnsureSuccessStatusCode();
        var review = await response.ToResponseModel<List<ActivitySummaryDto>>();

        review.Should().ContainSingle();
        review[0].Status.Should().Be("active");
    }
}