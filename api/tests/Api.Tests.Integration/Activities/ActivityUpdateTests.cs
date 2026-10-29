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
        var closed = await PostActivityAsync(
            new CreateActivityRequestDto("Closed record", categoryId, taskId, null, "closed", null, null, false, DefaultExpectedResults, ClosureReason: "No longer needed"));

        // A valid request in every other respect, so the refusal is the read-only rule and not validation.
        var response = await Client.PutAsJsonAsync(
            $"activities/{closed.Id}",
            new UpdateActivityRequestDto("Trying to edit", categoryId, taskId, null, "closed", null, null, false, DefaultExpectedResults, ClosureReason: "Changed reason"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // BLUM-7673: the editor's status screen can close a completed Planned/Active record, and that is the only moment its
    // reason can be recorded — the record is read-only from then on.
    [Fact]
    public async Task ShouldRecordTheClosureReason_WhenAnActiveRecordIsClosed()
    {
        AuthenticateAsMainOrgLead();
        var created = await CreateActiveActivityAsync("Closing soon");
        var (categoryId, taskId) = await GetCategoryAndTypeAsync();

        var response = await Client.PutAsJsonAsync(
            $"activities/{created.Id}",
            new UpdateActivityRequestDto("Closing soon", categoryId, taskId, null, "closed", 3, 2026, false, DefaultExpectedResults, ClosureReason: "  Funding withdrawn  "));

        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<ActivityDto>();
        dto.Status.Should().Be("closed");
        dto.ClosureReason.Should().Be("Funding withdrawn");
        dto.StartMonth.Should().BeNull("a start date belongs to an Active record only");

        var saved = await Context.Activities.AsNoTracking().FirstAsync(x => x.Id == new ActivityId(created.Id));
        saved.ClosureReason.Should().Be("Funding withdrawn");

        var audit = await Context.AuditLogs.AsNoTracking()
            .Where(x => x.TargetType == AuditTargetType.Activity && x.TargetId == created.Id && x.Action == AuditAction.ActivityUpdated)
            .ToListAsync();

        // Parsed rather than substring-matched: the column is jsonb, which re-spaces what was written.
        var after = JsonDocument.Parse(audit.Should().ContainSingle().Which.After!).RootElement;
        after.GetProperty("closureReason").GetString().Should().Be("Funding withdrawn");
    }

    [Fact]
    public async Task ShouldRejectClosingARecord_WithoutAReason()
    {
        AuthenticateAsMainOrgLead();
        var created = await CreatePlannedActivityAsync("Still planned");
        var (categoryId, taskId) = await GetCategoryAndTypeAsync();

        var response = await Client.PutAsJsonAsync(
            $"activities/{created.Id}",
            new UpdateActivityRequestDto("Still planned", categoryId, taskId, null, "closed", null, null, false, DefaultExpectedResults));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var saved = await Context.Activities.AsNoTracking().FirstAsync(x => x.Id == new ActivityId(created.Id));
        saved.Status!.ToString().Should().Be("planned");
    }

    // A completed record is saved for real whatever saveAsDraft says, so the flag must not switch its completion rules
    // off either — or a direct PUT could close it without a reason, which read-only it could then never be given.
    [Fact]
    public async Task ShouldRequireAClosureReason_WhenACompletedRecordClaimsToBeADraft()
    {
        AuthenticateAsMainOrgLead();
        var created = await CreateActiveActivityAsync("Closing soon");
        var (categoryId, taskId) = await GetCategoryAndTypeAsync();

        var response = await Client.PutAsJsonAsync(
            $"activities/{created.Id}",
            new UpdateActivityRequestDto("Closing soon", categoryId, taskId, null, "closed", null, null, SaveAsDraft: true, DefaultExpectedResults));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var envelope = await response.ToResponseModel<ValidationErrorEnvelope>();
        envelope.Error.Should().Be(ValidationErrorEnvelope.ValidationFailed);
        envelope.Errors.Should().ContainSingle(e => e.Property == "ClosureReason").Which.Message.Should().Be(ActivityClosureReason.RequiredError);

        var saved = await Context.Activities.AsNoTracking().FirstAsync(x => x.Id == new ActivityId(created.Id));
        saved.Status!.ToString().Should().Be("active");
        saved.IsComplete.Should().BeTrue();
    }

    [Theory]
    [InlineData(null, null, "The start date must include a month")]
    [InlineData(13, 2026, "The start date must be a real date")]
    public async Task ShouldApplyTheStartDateRules_WhenACompletedRecordClaimsToBeADraft(int? startMonth, int? startYear, string expectedMessage)
    {
        AuthenticateAsMainOrgLead();
        var created = await CreatePlannedActivityAsync("Starting now");
        var (categoryId, taskId) = await GetCategoryAndTypeAsync();

        var response = await Client.PutAsJsonAsync(
            $"activities/{created.Id}",
            new UpdateActivityRequestDto("Starting now", categoryId, taskId, null, "active", startMonth, startYear, SaveAsDraft: true, DefaultExpectedResults));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var envelope = await response.ToResponseModel<ValidationErrorEnvelope>();
        envelope.Errors.Should().Contain(e => e.Message == expectedMessage);

        var saved = await Context.Activities.AsNoTracking().FirstAsync(x => x.Id == new ActivityId(created.Id));
        saved.Status!.ToString().Should().Be("planned");
        saved.StartMonth.Should().BeNull();
    }

    // The counterpart: a record that really is still a draft keeps the draft rules, so it can be closed without a reason.
    [Fact]
    public async Task ShouldStillLetADraftBeClosedWithoutAReason()
    {
        AuthenticateAsMainOrgLead();
        var draft = await CreateDraftActivityAsync("Unfinished");

        var response = await Client.PutAsJsonAsync(
            $"activities/{draft.Id}",
            new UpdateActivityRequestDto("Unfinished", null, null, null, "closed", null, null, SaveAsDraft: true));

        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<ActivityDto>();
        dto.IsDraft.Should().BeTrue();
        dto.Status.Should().Be("closed");
        dto.ClosureReason.Should().BeNull();
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