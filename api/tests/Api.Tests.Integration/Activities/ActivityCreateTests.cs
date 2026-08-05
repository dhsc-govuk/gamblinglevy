using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
using Domain.Activities;
using Domain.AuditLogs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.Activities;

// Create activity record (BLUM redesign): category + type + user-chosen status, free-text expected results.
public class ActivityCreateTests(IntegrationTestWebFactory factory) : ActivityTestBase(factory)
{
    [Fact]
    public async Task ShouldCreateCompleteActivity_WithStatusAndExpectedResults_AndAuditLog()
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, taskId) = await GetCategoryAndTypeAsync();

        var expectedResults = new List<ExpectedResultInputDto>
        {
            new(null, "JSNA approved and published"),
            new(null, "Number of residents at risk"),
        };
        var request = new CreateActivityRequestDto("Joint strategic needs assessment", categoryId, taskId, null, "active", 3, 2026, false, expectedResults);
        var response = await Client.PostAsJsonAsync("activities", request);

        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<ActivityDto>();
        dto.Status.Should().Be("active");
        dto.IsDraft.Should().BeFalse();
        dto.StartMonth.Should().Be(3);
        dto.StartYear.Should().Be(2026);
        dto.ExpectedResults.Should().HaveCount(2);

        var saved = await Context.Activities.AsNoTracking().FirstAsync(x => x.Id == new ActivityId(dto.Id));
        saved.OrganisationId.Should().Be(MainOrg.Id);
        saved.IsComplete.Should().BeTrue();
        saved.Status!.ToString().Should().Be("active");

        var expected = await Context.ActivityExpectedResults.AsNoTracking().Where(x => x.ActivityId == saved.Id).ToListAsync();
        expected.Should().HaveCount(2);

        var audit = await Context.AuditLogs.AsNoTracking()
            .Where(x => x.TargetType == AuditTargetType.Activity && x.TargetId == saved.Id.Value)
            .ToListAsync();
        audit.Should().ContainSingle(x => x.Action == AuditAction.ActivityCreated);
    }

    [Fact]
    public async Task ShouldDefaultDisplayTitle_ToTypeName_WhenTitleOmitted()
    {
        AuthenticateAsMainOrgLead();
        var taxonomy = await GetTaxonomyAsync();
        var category = taxonomy.Categories.First(c => c.Name == "Needs assessment");
        var task = taxonomy.Tasks.First(t => t.CategoryId == category.Id);

        var response = await Client.PostAsJsonAsync(
            "activities",
            new CreateActivityRequestDto(null, category.Id, task.Id, null, "planned", null, null, false, DefaultExpectedResults));

        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<ActivityDto>();
        dto.Title.Should().Be(task.Name);
    }

    [Fact]
    public async Task ShouldRejectCreate_WhenMandatoryFieldsMissing()
    {
        AuthenticateAsMainOrgLead();

        var response = await Client.PostAsJsonAsync(
            "activities",
            new CreateActivityRequestDto(null, null, null, null, null, null, null, SaveAsDraft: false));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Context.Activities.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ShouldRejectActive_WhenStartDateMissing()
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, taskId) = await GetCategoryAndTypeAsync();

        var response = await Client.PostAsJsonAsync(
            "activities",
            new CreateActivityRequestDto("Active without date", categoryId, taskId, null, "active", null, null, false));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ShouldRejectActive_WhenStartDateInFuture()
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, taskId) = await GetCategoryAndTypeAsync();
        var futureYear = DateTime.UtcNow.Year + 1;

        var response = await Client.PostAsJsonAsync(
            "activities",
            new CreateActivityRequestDto("Active starting in the future", categoryId, taskId, null, "active", 6, futureYear, false, DefaultExpectedResults));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Start date must be this month or in the past");
        (await Context.Activities.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ShouldCreateActive_WhenStartDateIsCurrentMonth()
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, taskId) = await GetCategoryAndTypeAsync();
        var now = DateTime.UtcNow;

        var response = await Client.PostAsJsonAsync(
            "activities",
            new CreateActivityRequestDto("Active starting this month", categoryId, taskId, null, "active", now.Month, now.Year, false, DefaultExpectedResults));

        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<ActivityDto>();
        dto.StartMonth.Should().Be(now.Month);
        dto.StartYear.Should().Be(now.Year);
    }

    [Fact]
    public async Task ShouldAllowDraft_WithFutureStartDate()
    {
        AuthenticateAsMainOrgLead();
        var futureYear = DateTime.UtcNow.Year + 1;

        // Drafts skip the completion rules entirely, so a future start date must not block saving one.
        var response = await Client.PostAsJsonAsync(
            "activities",
            new CreateActivityRequestDto("Future draft", null, null, null, "active", 6, futureYear, true));

        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<ActivityDto>();
        dto.IsDraft.Should().BeTrue();
    }
}