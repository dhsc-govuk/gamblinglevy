using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Dtos;
using Application.Common.Features;
using Domain.AuditLogs;
using Domain.DataCollectionForms;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.DataCollectionForms;

// BLUM-9102: publishing a form module saves a fixed, numbered version (behind BLUM_9087). Every test sets the flag on
// its first line, because the settings singleton outlives a test.
public class FormModuleVersionTests(IntegrationTestWebFactory factory) : BaseIntegrationTest(factory), IAsyncLifetime
{
    private const string Route = "admin/data-collection-forms";

    private sealed record ModuleResult(Guid Id, string Status, int? LatestVersionNumber, bool? VersionCreated);

    private sealed record QuestionResult(Guid Id, string Code, string Label);

    private sealed record ModuleWithQuestionsResult(Guid Id, string Status, List<QuestionResult> Questions);

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
    public async Task ShouldSaveVersionOne_HoldingEverySectionQuestionAndOption_WhenADraftIsPublished()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var moduleId = await CreateModuleWithQuestionsAsync("v_first_publish");

        var published = await PublishAsync(moduleId);

        published.Status.Should().Be("published");
        published.LatestVersionNumber.Should().Be(1);
        published.VersionCreated.Should().BeTrue();

        var version = (await VersionsAsync(moduleId)).Should().ContainSingle().Subject;
        version.VersionNumber.Should().Be(1);
        version.ContentHash.Should().HaveLength(64);
        version.PublishedBy.Should().Be(AdminsData.MainAdmin.Id);

        using var definition = JsonDocument.Parse(version.Definition);
        var root = definition.RootElement;
        root.GetProperty("schemaVersion").GetInt32().Should().Be(1);
        root.GetProperty("code").GetString().Should().Be("v_first_publish");
        root.GetProperty("sections").EnumerateArray().Select(s => s.GetProperty("title").GetString())
            .Should().Equal("Your delivery");

        var questions = root.GetProperty("questions").EnumerateArray().ToList();
        questions.Select(q => q.GetProperty("code").GetString()).Should().Equal("Q_DELIVERED", "Q_NOTES");
        questions[0].GetProperty("isRequired").GetBoolean().Should().BeTrue();
        questions[0].GetProperty("sectionId").GetGuid().Should().NotBeEmpty();
        questions[0].GetProperty("options").EnumerateArray().Select(o => o.GetProperty("value").GetString())
            .Should().Equal("yes", "no");
        questions[1].GetProperty("isRequired").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ShouldSaveTheNextVersion_AndLeaveTheEarlierOneUnchanged_WhenAReopenedDraftIsChanged()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var moduleId = await CreateModuleWithQuestionsAsync("v_changed");
        await PublishAsync(moduleId);
        var v1 = (await VersionsAsync(moduleId)).Single();

        (await Client.PostAsync($"{Route}/modules/{moduleId}/draft", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var notes = (await GetModuleAsync(moduleId)).Questions.Single(q => q.Code == "Q_NOTES");
        var update = await Client.PutAsJsonAsync(
            $"{Route}/modules/{moduleId}/questions/{notes.Id}",
            new UpdateFormQuestionRequestDto(null, "Anything else to tell us?", "Textarea", false, null, null, null));
        update.IsSuccessStatusCode.Should().BeTrue();

        var published = await PublishAsync(moduleId);

        published.LatestVersionNumber.Should().Be(2);
        published.VersionCreated.Should().BeTrue();

        var versions = await VersionsAsync(moduleId);
        versions.Select(v => v.VersionNumber).Should().Equal(1, 2);
        versions[0].Definition.Should().Be(v1.Definition);
        versions[0].ContentHash.Should().Be(v1.ContentHash);
        versions[0].Definition.Should().NotContain("Anything else to tell us?");
        versions[1].Definition.Should().Contain("Anything else to tell us?");
        versions[1].ContentHash.Should().NotBe(v1.ContentHash);
    }

    [Fact]
    public async Task ShouldNotSaveANewVersion_WhenAReopenedDraftIsPublishedUnchanged()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var moduleId = await CreateModuleWithQuestionsAsync("v_unchanged");
        await PublishAsync(moduleId);
        (await Client.PostAsync($"{Route}/modules/{moduleId}/draft", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var published = await PublishAsync(moduleId);

        published.Status.Should().Be("published");
        published.LatestVersionNumber.Should().Be(1);
        published.VersionCreated.Should().BeFalse();
        (await VersionsAsync(moduleId)).Should().ContainSingle();
        (await AuditAsync(moduleId, AuditAction.DataCollectionFormModuleVersionPublished)).Should().ContainSingle();
    }

    [Theory]
    [InlineData("v_immutable_number", "UPDATE form_module_versions SET version_number = 99 WHERE form_module_id = {0}")]
    [InlineData("v_immutable_definition", "UPDATE form_module_versions SET definition = '{{}}'::jsonb WHERE form_module_id = {0}")]
    [InlineData("v_immutable_delete", "DELETE FROM form_module_versions WHERE form_module_id = {0}")]
    public async Task ShouldRefuseAnyChangeToASavedVersion(string code, string sql)
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var moduleId = await CreateModuleWithQuestionsAsync(code);
        await PublishAsync(moduleId);
        var before = (await VersionsAsync(moduleId)).Single();

        var change = () => Context.Database.ExecuteSqlRawAsync(sql, moduleId);

        (await change.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.RestrictViolation);
        (await VersionsAsync(moduleId)).Should().ContainSingle().Which.Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task ShouldAuditThePublishedVersion_WithTheModuleVersionNumberAndPublisher()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var moduleId = await CreateModuleWithQuestionsAsync("v_audited");

        await PublishAsync(moduleId);

        var row = (await AuditAsync(moduleId, AuditAction.DataCollectionFormModuleVersionPublished)).Should().ContainSingle().Subject;
        row.TargetType.Should().Be(AuditTargetType.DataCollectionForm);
        row.ActorUserId.Should().Be(AdminsData.MainAdmin.Id);
        row.Before.Should().BeNull();

        var version = (await VersionsAsync(moduleId)).Single();
        using var after = JsonDocument.Parse(row.After!);
        after.RootElement.GetProperty("moduleId").GetGuid().Should().Be(moduleId);
        after.RootElement.GetProperty("code").GetString().Should().Be("v_audited");
        after.RootElement.GetProperty("versionId").GetGuid().Should().Be(version.Id.Value);
        after.RootElement.GetProperty("versionNumber").GetInt32().Should().Be(1);
        after.RootElement.GetProperty("contentHash").GetString().Should().Be(version.ContentHash);
        (await AuditAsync(moduleId, AuditAction.DataCollectionFormModulePublished)).Should().ContainSingle();
    }

    [Fact]
    public async Task ShouldReopenAPublishedModuleAsADraft_AndAuditIt()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var moduleId = await CreateModuleWithQuestionsAsync("v_draft_started");
        await PublishAsync(moduleId);

        var response = await Client.PostAsync($"{Route}/modules/{moduleId}/draft", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ToResponseModel<ModuleResult>()).Status.Should().Be("draft");

        var module = await Context.FormModules.AsNoTracking().SingleAsync(m => m.Id == FormModuleId.From(moduleId));
        module.Status.IsPublished.Should().BeFalse();
        module.PublishedAt.Should().NotBeNull("a reopened draft keeps its last publish, which the module seeders read");

        var row = (await AuditAsync(moduleId, AuditAction.DataCollectionFormModuleDraftStarted)).Should().ContainSingle().Subject;
        row.ActorUserId.Should().Be(AdminsData.MainAdmin.Id);
        row.Before.Should().Contain("published");
        row.After.Should().Contain("draft");
    }

    [Fact]
    public async Task ShouldRefuseToStartADraft_WhenTheModuleIsNotPublished()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var moduleId = await CreateModuleWithQuestionsAsync("v_draft_of_draft");

        var response = await Client.PostAsync($"{Route}/modules/{moduleId}/draft", null);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await AuditAsync(moduleId, AuditAction.DataCollectionFormModuleDraftStarted)).Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldPublishWithoutSavingAVersion_WhenTheFlagIsOff()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: false);
        var moduleId = await CreateModuleWithQuestionsAsync("v_flag_off");

        var published = await PublishAsync(moduleId);

        published.Status.Should().Be("published");
        published.LatestVersionNumber.Should().BeNull();
        published.VersionCreated.Should().BeNull();
        (await VersionsAsync(moduleId)).Should().BeEmpty();
        (await AuditAsync(moduleId, AuditAction.DataCollectionFormModuleVersionPublished)).Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldHideTheDraftEndpoint_WhenTheFlagIsOff()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: false);
        var moduleId = await CreateModuleWithQuestionsAsync("v_flag_off_draft");
        await PublishAsync(moduleId);

        var response = await Client.PostAsync($"{Route}/modules/{moduleId}/draft", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Context.FormModules.AsNoTracking().SingleAsync(m => m.Id == FormModuleId.From(moduleId)))
            .Status.IsPublished.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldRefuseToRenameAPublishedModule_SoItsDetailsChangeOnlyThroughADraft()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var moduleId = await CreateModuleWithQuestionsAsync("v_rename_published");
        await PublishAsync(moduleId);

        var response = await Client.PutAsJsonAsync($"{Route}/modules/{moduleId}", new UpdateFormModuleRequestDto("Renamed", null));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("error").GetString().Should().Be("MODULE_NOT_EDITABLE");
        (await Context.FormModules.AsNoTracking().SingleAsync(m => m.Id == FormModuleId.From(moduleId)))
            .Name.Should().Be("Module v_rename_published");
    }

    [Fact]
    public async Task ShouldWithdrawAPublishedQuestion_RatherThanDeleteIt_AndFreeItsSection()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var moduleId = await CreateModuleWithQuestionsAsync("v_withdraw_question");
        await PublishAsync(moduleId);
        (await Client.PostAsync($"{Route}/modules/{moduleId}/draft", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var delivered = (await GetModuleAsync(moduleId)).Questions.Single(q => q.Code == "Q_DELIVERED");

        var response = await Client.DeleteAsync($"{Route}/modules/{moduleId}/questions/{delivered.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var row = await Context.DcFormQuestions.AsNoTracking().SingleAsync(q => q.Id == DcFormQuestionId.From(delivered.Id));
        row.IsActive.Should().BeFalse("answers older reports gave still need its label");
        row.SectionId.Should().BeNull();
        (await AuditAsync(delivered.Id, AuditAction.DataCollectionFormQuestionDeleted)).Should().ContainSingle()
            .Which.After.Should().NotBeNull();

        // Deleting it again changes nothing, so it writes no second row.
        (await Client.DeleteAsync($"{Route}/modules/{moduleId}/questions/{delivered.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AuditAsync(delivered.Id, AuditAction.DataCollectionFormQuestionDeleted)).Should().ContainSingle();

        // It no longer holds its section, so the section can go as it could before.
        var section = await Context.DcFormSections.AsNoTracking().SingleAsync(s => s.FormModuleId == FormModuleId.From(moduleId));
        (await Client.DeleteAsync($"{Route}/modules/{moduleId}/sections/{section.Id.Value}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ShouldRefuseToPublish_WhenEveryQuestionHasBeenWithdrawn()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var moduleId = await CreateModuleWithQuestionsAsync("v_all_withdrawn");
        await PublishAsync(moduleId);
        (await Client.PostAsync($"{Route}/modules/{moduleId}/draft", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        foreach (var question in (await GetModuleAsync(moduleId)).Questions)
        {
            (await Client.DeleteAsync($"{Route}/modules/{moduleId}/questions/{question.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var response = await Client.PostAsync($"{Route}/modules/{moduleId}/publish", null);

        // The withdrawn rows are still there, but no LA would see a question.
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("error").GetString().Should().Be("MODULE_EMPTY_ON_PUBLISH");
        (await VersionsAsync(moduleId)).Should().ContainSingle();
    }

    [Fact]
    public async Task ShouldDeleteAQuestionNoVersionPublished()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var moduleId = await CreateModuleWithQuestionsAsync("v_delete_unpublished");
        await PublishAsync(moduleId);
        (await Client.PostAsync($"{Route}/modules/{moduleId}/draft", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client.PostAsJsonAsync(
            $"{Route}/modules/{moduleId}/questions",
            new CreateFormQuestionRequestDto(null, "Q_DRAFT_ONLY", "A question never published", "Textarea", false, null, null, null)))
            .IsSuccessStatusCode.Should().BeTrue();
        var draftOnly = (await GetModuleAsync(moduleId)).Questions.Single(q => q.Code == "Q_DRAFT_ONLY");

        (await Client.DeleteAsync($"{Route}/modules/{moduleId}/questions/{draftOnly.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await Context.DcFormQuestions.AsNoTracking().AnyAsync(q => q.Id == DcFormQuestionId.From(draftOnly.Id))).Should().BeFalse();
    }

    [Fact]
    public async Task ShouldStillRenameAPublishedModule_WhenTheFlagIsOff()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: false);
        var moduleId = await CreateModuleWithQuestionsAsync("v_rename_flag_off");
        await PublishAsync(moduleId);

        var response = await Client.PutAsJsonAsync($"{Route}/modules/{moduleId}", new UpdateFormModuleRequestDto("Renamed", null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Context.FormModules.AsNoTracking().SingleAsync(m => m.Id == FormModuleId.From(moduleId)))
            .Name.Should().Be("Renamed");
    }

    // A module with one section holding a required radio question and an optional textarea outside it.
    private async Task<Guid> CreateModuleWithQuestionsAsync(string code)
    {
        var create = await Client.PostAsJsonAsync($"{Route}/modules", new CreateFormModuleRequestDto(code, $"Module {code}", null));
        create.IsSuccessStatusCode.Should().BeTrue();
        var moduleId = (await create.ToResponseModel<ModuleResult>()).Id;

        var section = await Client.PostAsJsonAsync(
            $"{Route}/modules/{moduleId}/sections",
            new CreateFormSectionRequestDto("Your delivery", null, null));
        section.IsSuccessStatusCode.Should().BeTrue();
        var sectionId = (await Context.DcFormSections.AsNoTracking().SingleAsync(s => s.FormModuleId == FormModuleId.From(moduleId))).Id.Value;

        var radio = await Client.PostAsJsonAsync(
            $"{Route}/modules/{moduleId}/questions",
            new CreateFormQuestionRequestDto(
                sectionId,
                "Q_DELIVERED",
                "Did you deliver the activity?",
                "Radio",
                true,
                null,
                null,
                new[] { new FormModuleQuestionOptionRequestDto("yes", "Yes"), new FormModuleQuestionOptionRequestDto("no", "No") }));
        radio.IsSuccessStatusCode.Should().BeTrue();

        var notes = await Client.PostAsJsonAsync(
            $"{Route}/modules/{moduleId}/questions",
            new CreateFormQuestionRequestDto(null, "Q_NOTES", "Any other notes?", "Textarea", false, null, null, null));
        notes.IsSuccessStatusCode.Should().BeTrue();

        return moduleId;
    }

    private async Task<ModuleResult> PublishAsync(Guid moduleId)
    {
        var response = await Client.PostAsync($"{Route}/modules/{moduleId}/publish", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.ToResponseModel<ModuleResult>();
    }

    private async Task<ModuleWithQuestionsResult> GetModuleAsync(Guid moduleId)
    {
        var response = await Client.GetAsync($"{Route}/modules/{moduleId}");
        response.IsSuccessStatusCode.Should().BeTrue();
        return await response.ToResponseModel<ModuleWithQuestionsResult>();
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