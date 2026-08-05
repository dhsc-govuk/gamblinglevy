using Api.Dtos;
using Domain.Activities;
using Domain.AuditLogs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.Activities;

// Soft-delete + restore: a deleted record leaves the main list, appears under "Deleted activities", and can be
// restored back to the list. The row is retained (soft delete) and both actions are audited.
public class ActivityDeleteTests(IntegrationTestWebFactory factory) : ActivityTestBase(factory)
{
    [Fact]
    public async Task ShouldSoftDelete_ExcludeFromList_AndAppearInDeleted()
    {
        AuthenticateAsMainOrgLead();
        var activity = await CreatePlannedActivityAsync();

        var response = await Client.DeleteAsync($"activities/{activity.Id}");
        response.IsSuccessStatusCode.Should().BeTrue();

        // Row retained with DeletedAt set.
        var saved = await Context.Activities.AsNoTracking().FirstAsync(x => x.Id == new ActivityId(activity.Id));
        saved.DeletedAt.Should().NotBeNull();

        var list = await (await Client.GetAsync("activities")).ToResponseModel<List<ActivitySummaryDto>>();
        list.Should().NotContain(x => x.Id == activity.Id);

        var deleted = await (await Client.GetAsync("activities/deleted")).ToResponseModel<List<ActivitySummaryDto>>();
        deleted.Should().Contain(x => x.Id == activity.Id);

        var audit = await Context.AuditLogs.AsNoTracking()
            .Where(x => x.TargetType == AuditTargetType.Activity && x.TargetId == activity.Id)
            .ToListAsync();
        audit.Should().Contain(x => x.Action == AuditAction.ActivityDeleted);
    }

    [Fact]
    public async Task ShouldRestoreDeleted_BackToList()
    {
        AuthenticateAsMainOrgLead();
        var activity = await CreatePlannedActivityAsync();
        await Client.DeleteAsync($"activities/{activity.Id}");

        var response = await Client.PostAsync($"activities/{activity.Id}/restore", null);
        response.IsSuccessStatusCode.Should().BeTrue();

        var list = await (await Client.GetAsync("activities")).ToResponseModel<List<ActivitySummaryDto>>();
        list.Should().Contain(x => x.Id == activity.Id);

        var deleted = await (await Client.GetAsync("activities/deleted")).ToResponseModel<List<ActivitySummaryDto>>();
        deleted.Should().NotContain(x => x.Id == activity.Id);

        var audit = await Context.AuditLogs.AsNoTracking()
            .Where(x => x.TargetType == AuditTargetType.Activity && x.TargetId == activity.Id)
            .ToListAsync();
        audit.Should().Contain(x => x.Action == AuditAction.ActivityRestored);
    }

    [Fact]
    public async Task ShouldAllowDeletingDraft()
    {
        AuthenticateAsMainOrgLead();
        var draft = await CreateDraftActivityAsync();

        var response = await Client.DeleteAsync($"activities/{draft.Id}");

        response.IsSuccessStatusCode.Should().BeTrue();
        var saved = await Context.Activities.AsNoTracking().FirstAsync(x => x.Id == new ActivityId(draft.Id));
        saved.DeletedAt.Should().NotBeNull();
    }
}