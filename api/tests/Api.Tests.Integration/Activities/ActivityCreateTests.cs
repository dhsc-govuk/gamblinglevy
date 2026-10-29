using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Common;
using Api.Dtos;
using Application.Activities.Commands;
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
    // Saved as a draft: the name is mandatory on completion since the 13 August protocol (BLUM-7620), so the
    // display-title fallback is only reachable before an activity is completed.
    public async Task ShouldDefaultDisplayTitle_ToTypeName_WhenTitleOmitted()
    {
        AuthenticateAsMainOrgLead();
        var taxonomy = await GetTaxonomyAsync();
        var category = taxonomy.Categories.First(c => c.Name == "Needs assessment");
        var task = taxonomy.Tasks.First(t => t.CategoryId == category.Id);

        var response = await Client.PostAsJsonAsync(
            "activities",
            new CreateActivityRequestDto(null, category.Id, task.Id, null, null, null, null, true, DefaultExpectedResults));

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
        (await response.Content.ReadAsStringAsync()).Should().Contain("The start date must be today or in the past");
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

    // BLUM-7673: a Closed activity records why it was closed.
    [Fact]
    public async Task ShouldCreateClosedActivity_WithItsClosureReason_AndAuditIt()
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, taskId) = await GetCategoryAndTypeAsync();

        var response = await Client.PostAsJsonAsync(
            "activities",
            new CreateActivityRequestDto("Closed early", categoryId, taskId, null, "closed", null, null, false, DefaultExpectedResults, ClosureReason: " No longer needed "));

        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<ActivityDto>();
        dto.Status.Should().Be("closed");
        dto.ClosureReason.Should().Be("No longer needed");

        var saved = await Context.Activities.AsNoTracking().FirstAsync(x => x.Id == new ActivityId(dto.Id));
        saved.ClosureReason.Should().Be("No longer needed");

        var fetched = await (await Client.GetAsync($"activities/{dto.Id}")).ToResponseModel<ActivityDto>();
        fetched.ClosureReason.Should().Be("No longer needed");

        var audit = await Context.AuditLogs.AsNoTracking()
            .Where(x => x.TargetType == AuditTargetType.Activity && x.TargetId == saved.Id.Value && x.Action == AuditAction.ActivityCreated)
            .ToListAsync();

        // Parsed rather than substring-matched: the column is jsonb, which re-spaces what was written.
        var after = JsonDocument.Parse(audit.Should().ContainSingle().Which.After!).RootElement;
        after.GetProperty("closureReason").GetString().Should().Be("No longer needed");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task ShouldRejectClosedActivity_WhenTheClosureReasonIsMissing(string? closureReason)
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, taskId) = await GetCategoryAndTypeAsync();

        var response = await Client.PostAsJsonAsync(
            "activities",
            new CreateActivityRequestDto("Closed early", categoryId, taskId, null, "closed", null, null, false, DefaultExpectedResults, ClosureReason: closureReason));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var envelope = await response.ToResponseModel<ValidationErrorEnvelope>();
        envelope.Error.Should().Be(ValidationErrorEnvelope.ValidationFailed);
        envelope.Errors.Should().ContainSingle(e => e.Property == "ClosureReason").Which.Message.Should().Be(ActivityClosureReason.RequiredError);
        (await Context.Activities.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ShouldAllowClosedDraft_WithoutAClosureReason()
    {
        AuthenticateAsMainOrgLead();

        var response = await Client.PostAsJsonAsync(
            "activities",
            new CreateActivityRequestDto("Closed draft", null, null, null, "closed", null, null, true));

        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<ActivityDto>();
        dto.IsDraft.Should().BeTrue();
        dto.ClosureReason.Should().BeNull();
    }

    // The cap is a column limit, so it binds on a draft too — a longer value would otherwise reach PostgreSQL and 500.
    [Fact]
    public async Task ShouldRejectAClosureReasonOver500Characters_EvenOnADraft()
    {
        AuthenticateAsMainOrgLead();

        var response = await Client.PostAsJsonAsync(
            "activities",
            new CreateActivityRequestDto("Closed draft", null, null, null, "closed", null, null, true, ClosureReason: new string('a', 501)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var envelope = await response.ToResponseModel<ValidationErrorEnvelope>();
        envelope.Errors.Should().ContainSingle(e => e.Property == "ClosureReason").Which.Message.Should().Be("Reason for closure must be 500 characters or less");
        (await Context.Activities.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ShouldAcceptAClosureReasonOfExactly500Characters()
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, taskId) = await GetCategoryAndTypeAsync();
        var reason = new string('a', 500);

        var response = await Client.PostAsJsonAsync(
            "activities",
            new CreateActivityRequestDto("Closed early", categoryId, taskId, null, "closed", null, null, false, DefaultExpectedResults, ClosureReason: reason));

        response.IsSuccessStatusCode.Should().BeTrue();
        (await response.ToResponseModel<ActivityDto>()).ClosureReason.Should().Be(reason);
    }

    // Only a Closed record keeps a reason, as only an Active one keeps a start date.
    [Theory]
    [InlineData("planned")]
    [InlineData("finished")]
    public async Task ShouldDropTheClosureReason_WhenTheStatusIsNotClosed(string status)
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, taskId) = await GetCategoryAndTypeAsync();

        var response = await Client.PostAsJsonAsync(
            "activities",
            new CreateActivityRequestDto("Not closed", categoryId, taskId, null, status, null, null, false, DefaultExpectedResults, ClosureReason: "Stale reason"));

        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<ActivityDto>();
        dto.ClosureReason.Should().BeNull();
        (await Context.Activities.AsNoTracking().FirstAsync(x => x.Id == new ActivityId(dto.Id))).ClosureReason.Should().BeNull();
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