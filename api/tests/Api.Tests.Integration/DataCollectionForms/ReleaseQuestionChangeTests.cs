using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Dtos;
using Application.Common.Features;
using Domain.AuditLogs;
using Domain.Collections;
using Domain.DataCollectionForms;
using Domain.Submissions;
using FluentAssertions;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Seed.FormVersions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.DataCollectionForms;

// BLUM-9105: question changes shipped in a release follow the version rules. A changed published form gets a new
// version on boot (FormModuleVersionSync); a collection whose window has not opened moves onto it, and one that has
// opened keeps its version (BLUM-9103); question codes and published option values stay fixed. Every test sets the flags it needs on its first line: the settings singleton outlives a test.
public class ReleaseQuestionChangeTests(IntegrationTestWebFactory factory) : BaseIntegrationTest(factory), IAsyncLifetime
{
    private const string FormsRoute = "admin/data-collection-forms";

    private sealed record ModuleResult(Guid Id);

    private sealed record QuestionResult(Guid Id, Guid? SectionId, string Code, string Label, string QuestionType, bool IsRequired, string? Hint, string? HelpText);

    private sealed record ModuleWithQuestionsResult(Guid Id, List<QuestionResult> Questions);

    private sealed record CollectionResult(Guid Id);

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
        await factory.SetFeatureAsync(Feature.BLUM_7637, enabled: false);
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: false);
        AuthenticateAsAdmin();
    }

    [Fact]
    public async Task ShouldSaveANewVersion_WhenAReleaseChangesAPublishedForm_AndMoveOnlyCollectionsNotYetOpen()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var moduleId = await CreatePublishedModuleAsync("rel_migration");
        var scheduled = await CreateCollectionAsync("Not open before the release", moduleId, windowOpen: false);
        var open = await CreateCollectionAsync("Open before the release", moduleId, windowOpen: true);
        var recorded = await RecordedVersionAsync(open);

        await Context.Database.ExecuteSqlRawAsync(
            "UPDATE dc_form_questions SET label = 'Reworded by a release' WHERE form_module_id = {0} AND code = 'Q_DELIVERED'", moduleId);
        await RunSyncAsync();

        var versions = await VersionsAsync(moduleId);
        versions.Select(v => v.VersionNumber).Should().Equal(1, 2);
        versions[1].Definition.Should().Contain("Reworded by a release");
        (await RecordedVersionAsync(scheduled)).Should().Be(versions[1].Id, "a collection uses the latest version until its window opens");
        (await RecordedVersionAsync(open)).Should().Be(recorded, "a collection whose window has opened keeps the version it recorded");

        var audit = await Context.AuditLogs.AsNoTracking()
            .Where(a => a.TargetId == moduleId && a.Action == AuditAction.DataCollectionFormModuleVersionPublished)
            .OrderBy(a => a.OccurredAt)
            .ToListAsync();
        audit.Last().After.Should().Contain("\"source\": \"release\"");

        var followed = await Context.AuditLogs.AsNoTracking()
            .Where(a => a.Action == AuditAction.CollectionFormVersionsFollowed && (a.TargetId == scheduled || a.TargetId == open))
            .ToListAsync();
        followed.Should().ContainSingle().Which.TargetId.Should().Be(scheduled);
        using var before = JsonDocument.Parse(followed[0].Before!);
        using var after = JsonDocument.Parse(followed[0].After!);
        after.RootElement.GetProperty("source").GetString().Should().Be("release");
        before.RootElement.GetProperty("formVersions").EnumerateArray().Single().GetProperty("versionNumber").GetInt32().Should().Be(1);
        var moved = after.RootElement.GetProperty("formVersions").EnumerateArray().Single();
        moved.GetProperty("versionNumber").GetInt32().Should().Be(2);
        moved.GetProperty("code").GetString().Should().Be("rel_migration");
    }

    [Fact]
    public async Task ShouldCatchUpACollectionNotYetOpen_ThatIsBehindAnEarlierVersion_AndSaySo()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var moduleId = await CreatePublishedModuleAsync("rel_catch_up");
        await Context.Database.ExecuteSqlRawAsync(
            "UPDATE dc_form_questions SET label = 'Reworded before the collection' WHERE form_module_id = {0} AND code = 'Q_DELIVERED'", moduleId);
        await RunSyncAsync();
        var versions = await VersionsAsync(moduleId);
        var collectionId = await CreateCollectionAsync("Behind from before the rule", moduleId, windowOpen: false);

        // As a collection published before BLUM-9103 would be: on version 1 although version 2 already exists.
        await Context.Database.ExecuteSqlRawAsync(
            "UPDATE collection_forms SET form_module_version_id = {0} WHERE collection_id = {1}", versions[0].Id.Value, collectionId);
        await RunSyncAsync();

        (await RecordedVersionAsync(collectionId)).Should().Be(versions[1].Id);
        var row = await Context.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.TargetId == collectionId && a.Action == AuditAction.CollectionFormVersionsFollowed);
        using var after = JsonDocument.Parse(row.After!);
        after.RootElement.GetProperty("source").GetString().Should().Be("catch_up", "no release changed the form on this boot");
    }

    [Fact]
    public async Task ShouldNotMoveACollectionNotYetOpen_OnceALocalAuthorityHasStartedAReport()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var moduleId = await CreatePublishedModuleAsync("rel_started");
        var collectionId = await CreateCollectionAsync("Started, then moved later", moduleId, windowOpen: false);
        var recorded = await RecordedVersionAsync(collectionId);

        // Only possible when DHSC moves an open window's date into the future after a report started.
        await StartReportAsync(collectionId);
        await Context.Database.ExecuteSqlRawAsync(
            "UPDATE dc_form_questions SET label = 'Reworded after a report started' WHERE form_module_id = {0} AND code = 'Q_DELIVERED'", moduleId);
        await RunSyncAsync();

        (await VersionsAsync(moduleId)).Should().HaveCount(2);
        (await RecordedVersionAsync(collectionId)).Should().Be(recorded, "a started report keeps the questions it began with");
    }

    [Fact]
    public async Task ShouldSaveNoVersion_WhenNothingChanged()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var moduleId = await CreatePublishedModuleAsync("rel_unchanged");

        await RunSyncAsync();

        (await VersionsAsync(moduleId)).Should().ContainSingle();
    }

    [Fact]
    public async Task ShouldLeaveADraftBeingEditedAlone()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var moduleId = await CreatePublishedModuleAsync("rel_draft");
        (await Client.PostAsync($"{FormsRoute}/modules/{moduleId}/draft", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        await Context.Database.ExecuteSqlRawAsync(
            "UPDATE dc_form_questions SET label = 'Unpublished edit' WHERE form_module_id = {0} AND code = 'Q_DELIVERED'", moduleId);
        await RunSyncAsync();

        (await VersionsAsync(moduleId)).Should().ContainSingle("a draft is DHSC's unpublished work");
    }

    [Fact]
    public async Task ShouldSaveTheRelease_AndMoveEveryRecordedVersionOntoIt_WhenTheFlagIsOff()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var moduleId = await CreatePublishedModuleAsync("rel_flag_off");
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: false);
        var open = await CreateCollectionAsync("Open, recorded at publish", moduleId, windowOpen: true);

        // A row left marked chosen by the removed version choice (MVP/UAT testing) is not treated differently.
        var chosen = await CreateCollectionAsync("Version chosen before BLUM-9103", moduleId, windowOpen: true);
        await Context.Database.ExecuteSqlRawAsync("UPDATE collection_forms SET is_version_chosen = true WHERE collection_id = {0}", chosen);
        var auditRowsBefore = await FollowedLiveAuditCountAsync();

        await Context.Database.ExecuteSqlRawAsync(
            "UPDATE dc_form_questions SET label = 'Changed with the flag off' WHERE form_module_id = {0} AND code = 'Q_DELIVERED'", moduleId);
        await RunSyncAsync();

        var versions = await VersionsAsync(moduleId);
        versions.Select(v => v.VersionNumber).Should().Equal(1, 2);
        versions[1].Definition.Should().Contain("Changed with the flag off");

        // With the flag off its LAs answer the live form, so switching the flag on must not hand them version 1, even
        // though the window has opened.
        (await RecordedVersionAsync(open)).Should().Be(versions[1].Id);
        (await RecordedVersionAsync(chosen)).Should().Be(versions[1].Id, "nothing reads is_version_chosen any more");
        (await FollowedLiveAuditCountAsync()).Should().Be(auditRowsBefore + 1);
    }

    [Fact]
    public async Task ShouldMoveNoRecordedVersion_WhenTheFlagIsOffAndNothingChanged()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var moduleId = await CreatePublishedModuleAsync("rel_flag_off_unchanged");
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: false);
        var collectionId = await CreateCollectionAsync("Already on the latest", moduleId, windowOpen: true);
        var recorded = await RecordedVersionAsync(collectionId);
        recorded.Should().NotBeNull();
        var auditRowsBefore = await FollowedLiveAuditCountAsync();

        await RunSyncAsync();

        (await RecordedVersionAsync(collectionId)).Should().Be(recorded);
        (await FollowedLiveAuditCountAsync()).Should().Be(auditRowsBefore, "a run that moves nothing writes no row");
    }

    // With the flag on the seeder leaves a published strategic_feedback alone (the test below), so a seeder change
    // reaches a deployed database only while the flag is off.
    [Fact]
    public async Task ShouldPublishTheStrategicFeedbackSeedersChange_AsANewVersion_WhenTheFlagIsOff()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: false);
        await factory.SetFeatureAsync(Feature.BLUM_7637, enabled: false);
        var moduleId = await StrategicFeedbackModuleIdAsync();
        var collectionId = await CreateCollectionAsync("Before the optional wording", moduleId, windowOpen: true);
        var before = (await VersionsAsync(moduleId)).Last().VersionNumber;

        // Switching BLUM_7637 re-runs the strategic-feedback seeder, which adds " (optional)" to the labels.
        await factory.SetFeatureAsync(Feature.BLUM_7637, enabled: true);

        var latest = (await VersionsAsync(moduleId)).Last();
        latest.VersionNumber.Should().Be(before + 1);
        latest.Definition.Should().Contain("(optional)");
        (await RecordedVersionAsync(collectionId)).Should().Be(latest.Id, "with the flag off every report reads the live form, so its recorded version follows it");
    }

    // BLUM-9105 (PO answer 3): DHSC owns the strategic feedback questions, so once they have edited them in the forms
    // builder no restart puts our wording back, not even one that switches BLUM_7637, and no release version is saved.
    [Fact]
    public async Task ShouldKeepDhscsEditsToTheStrategicFeedbackQuestions_ThroughAReboot()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        await factory.SetFeatureAsync(Feature.BLUM_7637, enabled: false);
        var moduleId = await StrategicFeedbackModuleIdAsync();
        (await Client.PostAsync($"{FormsRoute}/modules/{moduleId}/draft", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var questions = (await GetModuleAsync(moduleId)).Questions;
        var challenges = questions.Single(q => q.Code == "SF_CHALLENGES");
        challenges.IsRequired.Should().BeTrue("the seeder makes it required while BLUM_7637 is off");
        (await Client.PutAsJsonAsync(
            $"{FormsRoute}/modules/{moduleId}/questions/{challenges.Id}",
            new UpdateFormQuestionRequestDto(challenges.SectionId, "Reworded by DHSC", challenges.QuestionType, false, "A hint DHSC wrote", challenges.HelpText, null)))
            .IsSuccessStatusCode.Should().BeTrue();
        var learnings = questions.Single(q => q.Code == "SF_LEARNINGS");
        (await Client.PutAsJsonAsync(
            $"{FormsRoute}/modules/{moduleId}/questions/{learnings.Id}",
            new UpdateFormQuestionRequestDto(learnings.SectionId, learnings.Label, learnings.QuestionType, learnings.IsRequired, learnings.Hint, learnings.HelpText, [new("yes", "Yes, contact us"), new("no", "No")])))
            .IsSuccessStatusCode.Should().BeTrue();

        // Error copy the builder cannot edit, as an earlier release left it. This release's seeder would rewrite it.
        await Context.Database.ExecuteSqlRawAsync(
            "UPDATE dc_form_questions SET error_message = 'Copy an earlier release wrote' WHERE form_module_id = {0} AND code = 'SF_LEARNINGS'", moduleId);
        (await Client.PostAsync($"{FormsRoute}/modules/{moduleId}/publish", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var versions = (await VersionsAsync(moduleId)).Count;

        await RebootAsync();

        await ShouldHoldDhscsEditsAsync(moduleId);
        (await VersionsAsync(moduleId)).Should().HaveCount(versions, "a boot that changes nothing saves no release version");

        await factory.SetFeatureAsync(Feature.BLUM_7637, enabled: true);

        await ShouldHoldDhscsEditsAsync(moduleId);
        (await VersionsAsync(moduleId)).Should().HaveCount(versions, "BLUM_7637 no longer reaches a published strategic_feedback");
    }

    // Since BLUM_7637 no longer reaches the published questions while the flag is on, a restart that switches it warns
    // instead of changing nothing in silence.
    [Fact]
    public async Task ShouldWarnAtBoot_WhenBlum7637NoLongerChangesTheStrategicFeedbackQuestions()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        await factory.SetFeatureAsync(Feature.BLUM_7637, enabled: false);
        (await StrategicFeedbackSeedWarningsAsync()).Should().BeEmpty("the questions are required, as BLUM_7637 off makes them");

        await factory.SetFeatureAsync(Feature.BLUM_7637, enabled: true);

        (await StrategicFeedbackSeedWarningsAsync()).Should().ContainSingle()
            .Which.Should().Contain("BLUM_7637 is on").And.ContainAll("SF_OTHER_PREVENTION", "SF_CHALLENGES", "SF_DHSC_SUPPORT", "SF_LEARNINGS");
    }

    [Fact]
    public async Task ShouldLeaveAReopenedStrategicFeedbackDraftAlone_WhenTheSeederRuns()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        await factory.SetFeatureAsync(Feature.BLUM_7637, enabled: false);
        var moduleId = await StrategicFeedbackModuleIdAsync();
        (await Client.PostAsync($"{FormsRoute}/modules/{moduleId}/draft", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var question = (await GetModuleAsync(moduleId)).Questions.Single(q => q.Code == "SF_CHALLENGES");
        (await Client.PutAsJsonAsync(
            $"{FormsRoute}/modules/{moduleId}/questions/{question.Id}",
            new UpdateFormQuestionRequestDto(question.SectionId, "A DHSC edit in progress", question.QuestionType, question.IsRequired, question.Hint, question.HelpText, null)))
            .IsSuccessStatusCode.Should().BeTrue();

        await factory.SetFeatureAsync(Feature.BLUM_7637, enabled: true);

        (await GetModuleAsync(moduleId)).Questions.Single(q => q.Code == "SF_CHALLENGES").Label.Should().Be("A DHSC edit in progress");

        // Hand the module back published, so the other tests start from a published strategic_feedback.
        (await Client.PostAsync($"{FormsRoute}/modules/{moduleId}/publish", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ShouldRefuseToReuseAPublishedQuestionCode()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var moduleId = await CreatePublishedModuleAsync("rel_code_reuse");
        (await Client.PostAsync($"{FormsRoute}/modules/{moduleId}/draft", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var notes = (await GetModuleAsync(moduleId)).Questions.Single(q => q.Code == "Q_NOTES");
        (await Client.DeleteAsync($"{FormsRoute}/modules/{moduleId}/questions/{notes.Id}")).IsSuccessStatusCode.Should().BeTrue();

        var response = await AddQuestionAsync(moduleId, "Q_NOTES", "A different question", "Textarea", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ToResponseModel<ErrorResult>()).Error.Should().Be("QUESTION_CODE_RETIRED");
    }

    [Fact]
    public async Task ShouldAllowReusingACode_WhenTheFlagIsOff()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var moduleId = await CreatePublishedModuleAsync("rel_code_reuse_off");
        (await Client.PostAsync($"{FormsRoute}/modules/{moduleId}/draft", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var notes = (await GetModuleAsync(moduleId)).Questions.Single(q => q.Code == "Q_NOTES");

        // Off before the delete too: with the flag on a published question is withdrawn and keeps its code.
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: false);
        (await Client.DeleteAsync($"{FormsRoute}/modules/{moduleId}/questions/{notes.Id}")).IsSuccessStatusCode.Should().BeTrue();

        var response = await AddQuestionAsync(moduleId, "Q_NOTES", "A different question", "Textarea", null);

        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("changed", "yes", "maybe")]
    [InlineData("removed", "yes", null)]
    public async Task ShouldRefuseToChangeOrRemoveAPublishedOptionValue(string change, string first, string? second)
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var moduleId = await CreatePublishedModuleAsync($"rel_option_{change}");
        (await Client.PostAsync($"{FormsRoute}/modules/{moduleId}/draft", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var radio = (await GetModuleAsync(moduleId)).Questions.Single(q => q.Code == "Q_DELIVERED");
        var options = new List<FormModuleQuestionOptionRequestDto> { new(first, "Yes") };
        if (second is not null)
        {
            options.Add(new FormModuleQuestionOptionRequestDto(second, "Maybe"));
        }

        var response = await UpdateRadioAsync(moduleId, radio, options);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ToResponseModel<ErrorResult>()).Error.Should().Be("OPTION_VALUE_FIXED");
    }

    [Fact]
    public async Task ShouldAllowRelabellingAPublishedOption_AndAddingANewOne()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var moduleId = await CreatePublishedModuleAsync("rel_option_relabel");
        (await Client.PostAsync($"{FormsRoute}/modules/{moduleId}/draft", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var radio = (await GetModuleAsync(moduleId)).Questions.Single(q => q.Code == "Q_DELIVERED");

        var response = await UpdateRadioAsync(moduleId, radio, [new("yes", "Yes, fully"), new("no", "Not yet"), new("partly", "Partly")]);

        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
    }

    private Task<HttpResponseMessage> UpdateRadioAsync(Guid moduleId, QuestionResult radio, IReadOnlyList<FormModuleQuestionOptionRequestDto> options) =>
        Client.PutAsJsonAsync(
            $"{FormsRoute}/modules/{moduleId}/questions/{radio.Id}",
            new UpdateFormQuestionRequestDto(radio.SectionId, radio.Label, "Radio", radio.IsRequired, radio.Hint, radio.HelpText, options));

    // A published module with a required yes/no radio question and an optional textarea: version 1.
    private async Task<Guid> CreatePublishedModuleAsync(string code)
    {
        var create = await Client.PostAsJsonAsync($"{FormsRoute}/modules", new CreateFormModuleRequestDto(code, $"Module {code}", null));
        create.IsSuccessStatusCode.Should().BeTrue();
        var moduleId = (await create.ToResponseModel<ModuleResult>()).Id;

        (await AddQuestionAsync(moduleId, "Q_DELIVERED", "Did you deliver the activity?", "Radio", [new("yes", "Yes"), new("no", "No")])).IsSuccessStatusCode.Should().BeTrue();
        (await AddQuestionAsync(moduleId, "Q_NOTES", "Any other notes?", "Textarea", null)).IsSuccessStatusCode.Should().BeTrue();
        (await Client.PostAsync($"{FormsRoute}/modules/{moduleId}/publish", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        return moduleId;
    }

    private Task<HttpResponseMessage> AddQuestionAsync(Guid moduleId, string code, string label, string type, IReadOnlyList<FormModuleQuestionOptionRequestDto>? options) =>
        Client.PostAsJsonAsync($"{FormsRoute}/modules/{moduleId}/questions", new CreateFormQuestionRequestDto(null, code, label, type, false, null, null, options));

    private async Task<ModuleWithQuestionsResult> GetModuleAsync(Guid moduleId) =>
        await (await Client.GetAsync($"{FormsRoute}/modules/{moduleId}")).ToResponseModel<ModuleWithQuestionsResult>();

    private async Task<Guid> StrategicFeedbackModuleIdAsync() =>
        (await Context.FormModules.AsNoTracking().SingleAsync(m => m.Code == "strategic_feedback")).Id.Value;

    // The live rows, read from the database: still published, holding the edits DHSC made before the reboot and the error
    // copy an earlier release left.
    private async Task ShouldHoldDhscsEditsAsync(Guid moduleId)
    {
        var module = await Context.FormModules.AsNoTracking()
            .Include(m => m.Questions)
            .ThenInclude(q => q.Options)
            .SingleAsync(m => m.Id == FormModuleId.From(moduleId));
        module.Status.IsPublished.Should().BeTrue();

        var challenges = module.Questions.Single(q => q.Code == "SF_CHALLENGES");
        challenges.Label.Should().Be("Reworded by DHSC");
        challenges.IsRequired.Should().BeFalse();
        challenges.Hint.Should().Be("A hint DHSC wrote");

        var learnings = module.Questions.Single(q => q.Code == "SF_LEARNINGS");
        learnings.ErrorMessage.Should().Be("Copy an earlier release wrote");
        learnings.Options
            .Where(o => o.IsActive)
            .OrderBy(o => o.DisplayOrder)
            .Select(o => (o.Value, o.Label))
            .Should().Equal(("yes", "Yes, contact us"), ("no", "No"));
    }

    // The whole seed a restart runs, FormModuleVersionSync included.
    private async Task RebootAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>().SeedDataAsync();
    }

    // Runs the flag-dependent seed through an initialiser whose log the test can read, and returns the strategic-feedback
    // seeder's warnings.
    private async Task<List<string>> StrategicFeedbackSeedWarningsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var logger = new WarningLogger<ApplicationDbContextInitialiser>();
        await ActivatorUtilities.CreateInstance<ApplicationDbContextInitialiser>(scope.ServiceProvider, logger).ApplyFeatureFlagsAsync();
        return logger.Warnings.Where(w => w.StartsWith("Strategic feedback module seed:", StringComparison.Ordinal)).ToList();
    }

    private sealed class WarningLogger<T> : ILogger<T>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }

    private Task<int> FollowedLiveAuditCountAsync() =>
        Context.AuditLogs.AsNoTracking().CountAsync(a => a.Action == AuditAction.CollectionFormVersionsFollowedLive);

    // A collection whose window has opened keeps its versions; one that has not follows the latest (BLUM-9103).
    private async Task<Guid> CreateCollectionAsync(string name, Guid moduleId, bool windowOpen)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (openDate, deadline) = windowOpen ? (today.AddDays(-5), today.AddDays(25)) : (today.AddDays(5), today.AddDays(35));
        var request = new CreateCollectionRequestDto(name, null, "quarterly_report", "2026/27", 1, false, openDate, deadline, true, false, null, [moduleId], null);

        var response = await Client.PostAsJsonAsync("data-collections", request);
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        return (await response.ToResponseModel<CollectionResult>()).Id;
    }

    private async Task StartReportAsync(Guid collectionId)
    {
        if (!await Context.Organisations.AsNoTracking().AnyAsync(o => o.Id == OrganisationsData.MainOrganisation.Id))
        {
            await Context.Organisations.AddAsync(OrganisationsData.MainOrganisation);
        }

        await Context.Submissions.AddAsync(Submission.Start(
            SubmissionId.New(),
            new CollectionId(collectionId),
            OrganisationsData.MainOrganisation.Id,
            Array.Empty<(FormModuleId ModuleId, string ModuleCode)>(),
            AdminsData.MainAdmin.Id,
            DateTime.UtcNow));
        await SaveChangesAsync();
    }

    private async Task<FormModuleVersionId?> RecordedVersionAsync(Guid collectionId) =>
        (await Context.CollectionForms.AsNoTracking().SingleAsync(f => f.CollectionId == new CollectionId(collectionId))).FormModuleVersionId;

    private Task<List<FormModuleVersion>> VersionsAsync(Guid moduleId) =>
        Context.FormModuleVersions.AsNoTracking()
            .Where(v => v.FormModuleId == FormModuleId.From(moduleId))
            .OrderBy(v => v.VersionNumber)
            .ToListAsync();

    private async Task RunSyncAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<FormModuleVersionSync>().RunAsync();
    }
}