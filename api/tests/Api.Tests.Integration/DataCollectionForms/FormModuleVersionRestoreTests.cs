using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Dtos;
using Application.Common.Features;
using Application.DataCollectionForms.Versions;
using Domain.AuditLogs;
using Domain.DataCollectionForms;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.DataCollectionForms;

// BLUM-9102: listing, viewing and restoring a form module's saved versions (behind BLUM_9087). Every test sets the
// flag on its first line, because the settings singleton outlives a test.
public class FormModuleVersionRestoreTests(IntegrationTestWebFactory factory) : BaseIntegrationTest(factory), IAsyncLifetime
{
    private const string Route = "admin/data-collection-forms";

    private sealed record ModuleResult(Guid Id, string Status, int? LatestVersionNumber, bool? VersionCreated);

    private sealed record OptionResult(string Value, string Label, bool IsActive);

    private sealed record QuestionResult(Guid Id, Guid? SectionId, string Code, string Label, string QuestionType, bool IsRequired, bool IsActive, List<OptionResult> Options);

    private sealed record SectionResult(Guid Id, int SectionNumber, string Title);

    private sealed record ModuleWithChildrenResult(Guid Id, string Name, string Status, List<SectionResult> Sections, List<QuestionResult> Questions);

    private sealed record VersionSummaryResult(Guid Id, int VersionNumber, DateTime PublishedAt, Guid PublishedBy, string? PublishedByName);

    private sealed record VersionResult(int VersionNumber, string Code, string Name, List<SectionResult> Sections, List<QuestionResult> Questions);

    private sealed record ErrorResult(string Error);

    public async Task InitializeAsync()
    {
        if (!await Context.Users.AsNoTracking().AnyAsync(u => u.Id == AdminsData.MainAdmin.Id))
        {
            await Context.Users.AddAsync(AdminsData.MainAdmin);
            await SaveChangesAsync();
        }

        AuthenticateAsAdmin();
    }

    public async Task DisposeAsync()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: false);
        AuthenticateAsAdmin();
    }

    [Fact]
    public async Task ShouldListVersionsNewestFirst_WithWhoPublishedThem()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var moduleId = await CreateTwoVersionsAsync("r_list");

        var response = await Client.GetAsync($"{Route}/modules/{moduleId}/versions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var versions = await response.ToResponseModel<List<VersionSummaryResult>>();
        versions.Select(v => v.VersionNumber).Should().Equal(2, 1);
        versions.Should().AllSatisfy(v =>
        {
            v.PublishedBy.Should().Be(AdminsData.MainAdmin.Id.Value);
            v.PublishedByName.Should().Be(AdminsData.MainAdmin.Name.ToString());
        });
    }

    [Fact]
    public async Task ShouldShowWhatAVersionHeld_NotWhatTheFormHoldsNow()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var moduleId = await CreateTwoVersionsAsync("r_view");

        var response = await Client.GetAsync($"{Route}/modules/{moduleId}/versions/1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var version = await response.ToResponseModel<VersionResult>();
        version.VersionNumber.Should().Be(1);
        version.Code.Should().Be("r_view");
        version.Sections.Select(s => s.Title).Should().Equal("Your delivery");
        version.Questions.Select(q => q.Code).Should().Equal("Q_DELIVERED", "Q_NOTES");
        var radio = version.Questions[0];
        radio.Label.Should().Be("Did you deliver the activity?");
        radio.IsRequired.Should().BeTrue();
        radio.Options.Select(o => o.Value).Should().Equal("yes", "no");
    }

    [Fact]
    public async Task ShouldAnswerNotFound_ForAVersionThatDoesNotExist()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var moduleId = await CreateTwoVersionsAsync("r_missing");

        var view = await Client.GetAsync($"{Route}/modules/{moduleId}/versions/9");
        var restore = await Client.PostAsync($"{Route}/modules/{moduleId}/versions/9/restore", null);

        view.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await view.ToResponseModel<ErrorResult>()).Error.Should().Be("VERSION_NOT_FOUND");
        restore.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await restore.ToResponseModel<ErrorResult>()).Error.Should().Be("VERSION_NOT_FOUND");
    }

    [Fact]
    public async Task ShouldReopenAPublishedForm_AndReplaceItsContentWithTheVersions()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var moduleId = await CreateTwoVersionsAsync("r_restore");

        var response = await Client.PostAsync($"{Route}/modules/{moduleId}/versions/1/restore", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var restored = await response.ToResponseModel<ModuleWithChildrenResult>();
        restored.Status.Should().Be("draft");
        restored.Sections.Should().ContainSingle().Which.Title.Should().Be("Your delivery");
        var radio = restored.Questions.Single(q => q.Code == "Q_DELIVERED");
        radio.Label.Should().Be("Did you deliver the activity?");
        radio.Options.Where(o => o.IsActive).Select(o => o.Value).Should().Equal("yes", "no");
        radio.Options.Should().ContainSingle(o => o.Value == "partly")
            .Which.IsActive.Should().BeFalse("a value version 2 published is kept, withdrawn, so its answers keep their meaning");
        radio.SectionId.Should().Be(restored.Sections.Single().Id);
        restored.Questions.Single(q => q.Code == "Q_NOTES").SectionId.Should().BeNull();

        var row = (await AuditAsync(moduleId, AuditAction.DataCollectionFormModuleVersionRestored)).Should().ContainSingle().Subject;
        row.ActorUserId.Should().Be(AdminsData.MainAdmin.Id);
        using var after = JsonDocument.Parse(row.After!);
        after.RootElement.GetProperty("restoredVersionNumber").GetInt32().Should().Be(1);
        after.RootElement.GetProperty("reopenedFromPublished").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ShouldSaveTheNextVersion_WhenARestoredVersionIsPublished_AndLeaveHistoryAlone()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var moduleId = await CreateTwoVersionsAsync("r_republish");
        var before = await VersionsAsync(moduleId);

        (await Client.PostAsync($"{Route}/modules/{moduleId}/versions/1/restore", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var published = await PublishAsync(moduleId);

        published.LatestVersionNumber.Should().Be(3);
        published.VersionCreated.Should().BeTrue();

        var versions = await VersionsAsync(moduleId);
        versions.Select(v => v.VersionNumber).Should().Equal(1, 2, 3);
        versions[0].Should().BeEquivalentTo(before[0]);
        versions[1].Should().BeEquivalentTo(before[1]);
        // Version 1's content again, plus what version 2 published and version 1 lacked, withdrawn.
        var radio = FormModuleDefinition.Parse(versions[2].Definition).Questions.Single(q => q.Code == "Q_DELIVERED");
        radio.Label.Should().Be("Did you deliver the activity?");
        radio.Options.Where(o => o.IsActive).Select(o => o.Value).Should().Equal("yes", "no");
        radio.Options.Should().ContainSingle(o => o.Value == "partly").Which.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task ShouldKeepAQuestionALaterVersionPublished_Withdrawn_WhenAnOlderVersionIsRestored()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var moduleId = await CreateTwoVersionsAsync("r_later_question");
        (await Client.PostAsync($"{Route}/modules/{moduleId}/draft", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client.PostAsJsonAsync(
            $"{Route}/modules/{moduleId}/questions",
            new CreateFormQuestionRequestDto(
                null,
                "Q_EXTRA",
                "Anything else?",
                "Radio",
                false,
                null,
                null,
                new[] { new FormModuleQuestionOptionRequestDto("yes", "Yes"), new FormModuleQuestionOptionRequestDto("no", "No") })))
            .IsSuccessStatusCode.Should().BeTrue();
        (await PublishAsync(moduleId)).LatestVersionNumber.Should().Be(3);

        (await Client.PostAsync($"{Route}/modules/{moduleId}/versions/1/restore", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var extra = (await GetModuleAsync(moduleId)).Questions.Single(q => q.Code == "Q_EXTRA");
        extra.IsActive.Should().BeFalse("LAs no longer see it, but answers older reports gave keep their label");
        extra.Label.Should().Be("Anything else?");
        extra.SectionId.Should().BeNull();
        extra.Options.Should().OnlyContain(o => o.IsActive, "its options are as published, so an older answer still finds its label");
    }

    [Fact]
    public async Task ShouldRefuseToRestore_IntoAnArchivedForm()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var moduleId = await CreateTwoVersionsAsync("r_archived");
        (await Client.DeleteAsync($"{Route}/modules/{moduleId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await Client.PostAsync($"{Route}/modules/{moduleId}/versions/1/restore", null);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ToResponseModel<ErrorResult>()).Error.Should().Be("MODULE_ARCHIVED");
        (await AuditAsync(moduleId, AuditAction.DataCollectionFormModuleVersionRestored)).Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldHideTheVersionEndpoints_WhenTheFlagIsOff()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var moduleId = await CreateTwoVersionsAsync("r_flag_off");
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: false);

        (await Client.GetAsync($"{Route}/modules/{moduleId}/versions")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Client.GetAsync($"{Route}/modules/{moduleId}/versions/1")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Client.PostAsync($"{Route}/modules/{moduleId}/versions/1/restore", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AuditAsync(moduleId, AuditAction.DataCollectionFormModuleVersionRestored)).Should().BeEmpty();
    }

    // Version 1: one section holding a required yes/no radio question, and an optional textarea outside it.
    // Version 2: the radio question reworded with a third option.
    private async Task<Guid> CreateTwoVersionsAsync(string code)
    {
        var create = await Client.PostAsJsonAsync($"{Route}/modules", new CreateFormModuleRequestDto(code, $"Module {code}", null));
        create.IsSuccessStatusCode.Should().BeTrue();
        var moduleId = (await create.ToResponseModel<ModuleResult>()).Id;

        (await Client.PostAsJsonAsync($"{Route}/modules/{moduleId}/sections", new CreateFormSectionRequestDto("Your delivery", null, null)))
            .IsSuccessStatusCode.Should().BeTrue();
        var sectionId = (await GetModuleAsync(moduleId)).Sections.Single().Id;

        (await Client.PostAsJsonAsync(
            $"{Route}/modules/{moduleId}/questions",
            new CreateFormQuestionRequestDto(
                sectionId,
                "Q_DELIVERED",
                "Did you deliver the activity?",
                "Radio",
                true,
                null,
                null,
                new[] { new FormModuleQuestionOptionRequestDto("yes", "Yes"), new FormModuleQuestionOptionRequestDto("no", "No") })))
            .IsSuccessStatusCode.Should().BeTrue();
        (await Client.PostAsJsonAsync(
            $"{Route}/modules/{moduleId}/questions",
            new CreateFormQuestionRequestDto(null, "Q_NOTES", "Any other notes?", "Textarea", false, null, null, null)))
            .IsSuccessStatusCode.Should().BeTrue();
        (await PublishAsync(moduleId)).LatestVersionNumber.Should().Be(1);

        (await Client.PostAsync($"{Route}/modules/{moduleId}/draft", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var radio = (await GetModuleAsync(moduleId)).Questions.Single(q => q.Code == "Q_DELIVERED");
        (await Client.PutAsJsonAsync(
            $"{Route}/modules/{moduleId}/questions/{radio.Id}",
            new UpdateFormQuestionRequestDto(
                sectionId,
                "Was the activity delivered?",
                "Radio",
                true,
                null,
                null,
                new[]
                {
                    new FormModuleQuestionOptionRequestDto("yes", "Yes"),
                    new FormModuleQuestionOptionRequestDto("partly", "Partly"),
                    new FormModuleQuestionOptionRequestDto("no", "No"),
                })))
            .IsSuccessStatusCode.Should().BeTrue();
        (await PublishAsync(moduleId)).LatestVersionNumber.Should().Be(2);

        return moduleId;
    }

    private async Task<ModuleResult> PublishAsync(Guid moduleId)
    {
        var response = await Client.PostAsync($"{Route}/modules/{moduleId}/publish", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.ToResponseModel<ModuleResult>();
    }

    private async Task<ModuleWithChildrenResult> GetModuleAsync(Guid moduleId)
    {
        var response = await Client.GetAsync($"{Route}/modules/{moduleId}");
        response.IsSuccessStatusCode.Should().BeTrue();
        return await response.ToResponseModel<ModuleWithChildrenResult>();
    }

    private Task<List<FormModuleVersion>> VersionsAsync(Guid moduleId) =>
        Context.FormModuleVersions.AsNoTracking()
            .Where(v => v.FormModuleId == FormModuleId.From(moduleId))
            .OrderBy(v => v.VersionNumber)
            .ToListAsync();

    private Task<List<AuditLog>> AuditAsync(Guid moduleId, string action) =>
        Context.AuditLogs.AsNoTracking()
            .Where(a => a.TargetId == moduleId && a.Action == action)
            .ToListAsync();
}