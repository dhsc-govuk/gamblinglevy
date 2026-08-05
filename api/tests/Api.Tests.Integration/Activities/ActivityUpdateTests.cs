using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
using Domain.AuditLogs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.Activities;

// Edit activity record: editable while Planned/Active, before/after audit, read-only once Closed/Finished, and
// category/type locked once setup is complete.
public class ActivityUpdateTests(IntegrationTestWebFactory factory) : ActivityTestBase(factory)
{
    [Fact]
    public async Task ShouldEditPlanned_AndWriteBeforeAfterAudit()
    {
        AuthenticateAsMainOrgLead();
        var created = await CreatePlannedActivityAsync("Original title");
        var (categoryId, taskId) = await GetCategoryAndTypeAsync();

        var response = await Client.PutAsJsonAsync(
            $"activities/{created.Id}",
            new UpdateActivityRequestDto("Updated title", categoryId, taskId, null, "planned", null, null, false, DefaultExpectedResults));

        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<ActivityDto>();
        dto.Title.Should().Be("Updated title");
        dto.Status.Should().Be("planned");

        var audit = await Context.AuditLogs.AsNoTracking()
            .Where(x => x.TargetType == AuditTargetType.Activity && x.TargetId == created.Id && x.Action == AuditAction.ActivityUpdated)
            .ToListAsync();
        audit.Should().ContainSingle();
        audit[0].Before.Should().NotBeNullOrEmpty();
        audit[0].After.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ShouldRejectEdit_WhenRecordIsClosed()
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, taskId) = await GetCategoryAndTypeAsync();
        var closed = await PostActivityAsync(new CreateActivityRequestDto("Closed record", categoryId, taskId, null, "closed", null, null, false, DefaultExpectedResults));

        var response = await Client.PutAsJsonAsync(
            $"activities/{closed.Id}",
            new UpdateActivityRequestDto("Trying to edit", categoryId, taskId, null, "closed", null, null, false, DefaultExpectedResults));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task ShouldLockCategoryAndType_OnceComplete()
    {
        AuthenticateAsMainOrgLead();
        var created = await CreatePlannedActivityAsync("Locked taxonomy");
        var taxonomy = await GetTaxonomyAsync();
        var differentCategory = taxonomy.Categories.First(c => c.Id != created.CategoryId);
        var differentTask = taxonomy.Tasks.First(t => t.CategoryId == differentCategory.Id);

        var response = await Client.PutAsJsonAsync(
            $"activities/{created.Id}",
            new UpdateActivityRequestDto("Same title", differentCategory.Id, differentTask.Id, null, "planned", null, null, false, DefaultExpectedResults));

        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<ActivityDto>();

        // Category and type are ignored for a completed record; they stay as originally chosen.
        dto.CategoryId.Should().Be(created.CategoryId);
        dto.TaskId.Should().Be(created.TaskId);
    }
}