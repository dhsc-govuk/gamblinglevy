using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Dtos;
using Application.Common.Features;
using Application.MaturityAssessments.Protocol;
using Domain.AuditLogs;
using Domain.Collections;
using Domain.DataCollectionForms;
using Domain.Submissions;
using FluentAssertions;
using Infrastructure.Persistence.Seed.FormVersions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Collections;

// BLUM-9103: a published collection records the version of each form it uses. It follows each form's latest version
// until its window opens (status Scheduled); from then on a version DHSC publishes does not change it. There is no
// per-collection version choice: the request's formVersions is accepted and ignored. Recording is not behind BLUM_9087;
// publishing a new version of a form is, so the tests that need one switch the flag on.
public class CollectionFormVersionTests(IntegrationTestWebFactory factory) : BaseIntegrationTest(factory), IAsyncLifetime
{
    private const string Route = "data-collections";
    private const string FormsRoute = "admin/data-collection-forms";

    private sealed record CollectionResult(Guid Id, bool IsDraft);

    private sealed record QuestionResult(Guid Id, Guid? SectionId, string Code, string Label, string QuestionType, bool IsRequired);

    private sealed record ModuleResult(Guid Id, List<QuestionResult> Questions);

    private sealed record PublishResult(int? LatestVersionNumber);

    private sealed record CollectionFormResult(Guid ModuleId, int? VersionNumber, bool VersionChosen);

    private sealed record CollectionWithFormsResult(Guid Id, List<CollectionFormResult> Forms);

    private sealed record VersionOptionResult(int VersionNumber, DateTime PublishedAt);

    private sealed record FormOptionResult(Guid ModuleId, string Type, List<VersionOptionResult>? Versions);

    public async Task InitializeAsync()
    {
        if (!await Context.Users.AsNoTracking().AnyAsync(u => u.Id == AdminsData.MainAdmin.Id))
        {
            await Context.Users.AddAsync(AdminsData.MainAdmin);
            await SaveChangesAsync();
        }

        Context.Collections.RemoveRange(Context.Collections);
        await SaveChangesAsync();
        AuthenticateAsAdmin();
    }

    public async Task DisposeAsync()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: false);
        AuthenticateAsAdmin();
    }

    [Fact]
    public async Task ShouldGiveEveryPublishedSeededFormAVersionOne_AtBoot()
    {
        var seeded = await Context.FormModules.AsNoTracking().ToListAsync();

        var withVersionOne = await Context.FormModuleVersions.AsNoTracking()
            .Where(v => v.VersionNumber == 1)
            .Select(v => v.FormModuleId)
            .ToListAsync();

        var published = seeded.Where(m => m.PublishedAt != null).Select(m => m.Id).ToList();
        published.Should().NotBeEmpty();
        withVersionOne.Should().Contain(published);

        // funding_spent is seeded as an empty draft and never published: it has nothing to version.
        withVersionOne.Should().NotContain(seeded.Single(m => m.Code == "funding_spent").Id);
    }

    [Fact]
    public async Task ShouldRecordTheLatestVersionOfEachForm_WhenACollectionIsCreatedPublished()
    {
        var modules = await ModuleIdsAsync("strategic_feedback", "activity_key_results");

        var id = await CreateAsync(Request("Published at create", saveAsDraft: false, windowOpen: false, modules));

        var forms = await FormsAsync(id);
        forms.Should().HaveCount(2);
        foreach (var form in forms)
        {
            form.FormModuleVersionId.Should().Be(await LatestVersionIdAsync(form.FormModuleId));
        }
    }

    [Fact]
    public async Task ShouldRecordNoVersions_UntilADraftCollectionIsPublished()
    {
        var modules = await ModuleIdsAsync("strategic_feedback");
        var id = await CreateAsync(Request("Draft first", saveAsDraft: true, windowOpen: false, modules));

        (await FormsAsync(id)).Should().ContainSingle().Which.FormModuleVersionId.Should().BeNull();

        await UpdateAsync(id, saveAsDraft: false, windowOpen: false, modules);

        (await FormsAsync(id)).Should().ContainSingle().Which.FormModuleVersionId.Should().Be(await LatestVersionIdAsync(modules[0]));
    }

    [Fact]
    public async Task ShouldMoveOntoANewVersion_UntilItsWindowOpens_AndKeepItsVersionOnceOpen()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var module = (await ModuleIdsAsync("strategic_feedback"))[0];
        var scheduled = await CreateAsync(Request("Not open yet", saveAsDraft: false, windowOpen: false, [module]));
        var open = await CreateAsync(Request("Already open", saveAsDraft: false, windowOpen: true, [module]));
        var original = (await FormsAsync(scheduled)).Single().FormModuleVersionId;
        (await FormsAsync(open)).Single().FormModuleVersionId.Should().Be(original);

        var newVersion = await PublishChangedVersionAsync(module);

        var latest = await LatestVersionIdAsync(module);
        latest.Should().NotBe(original);
        (await FormsAsync(scheduled)).Single().FormModuleVersionId.Should().Be(latest, "a collection uses the latest version until its window opens");
        (await FormsAsync(open)).Single().FormModuleVersionId.Should().Be(original, "from the open date on its versions are fixed");

        var audit = await FollowedAuditRowsAsync(scheduled);
        audit.Should().ContainSingle();
        using (var after = JsonDocument.Parse(audit[0].After!))
        {
            after.RootElement.GetProperty("source").GetString().Should().Be("publish");
            var moved = after.RootElement.GetProperty("formVersions").EnumerateArray().Single();
            moved.GetProperty("moduleId").GetGuid().Should().Be(module);
            moved.GetProperty("versionNumber").GetInt32().Should().Be(newVersion);
        }

        (await FollowedAuditRowsAsync(open)).Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldCatchUpACollectionNotYetOpen_WhenTheFormIsRepublishedUnchanged()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var module = (await ModuleIdsAsync("strategic_feedback"))[0];
        await PublishChangedVersionAsync(module);
        var latest = await LatestVersionIdAsync(module);
        var id = await CreateAsync(Request("Behind the latest", saveAsDraft: false, windowOpen: false, [module]));

        // As a collection published before BLUM-9103 would be: on version 1 although a later version exists.
        var versionOne = (await Context.FormModuleVersions.AsNoTracking().SingleAsync(v => v.FormModuleId == FormModuleId.From(module) && v.VersionNumber == 1)).Id;
        await Context.Database.ExecuteSqlRawAsync(
            "UPDATE collection_forms SET form_module_version_id = {0} WHERE collection_id = {1}", versionOne.Value, id);

        (await Client.PostAsync($"{FormsRoute}/modules/{module}/draft", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var publish = await Client.PostAsync($"{FormsRoute}/modules/{module}/publish", null);

        publish.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LatestVersionIdAsync(module)).Should().Be(latest, "the content was unchanged, so no version was saved");
        (await FormsAsync(id)).Single().FormModuleVersionId.Should().Be(latest);
        (await FollowedAuditRowsAsync(id)).Should().ContainSingle();
    }

    [Fact]
    public async Task ShouldStopFollowing_OnceItsOpenDatePasses()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var module = (await ModuleIdsAsync("strategic_feedback"))[0];
        var id = await CreateAsync(Request("Opens between publishes", saveAsDraft: false, windowOpen: false, [module]));

        await PublishChangedVersionAsync(module);
        var takenBeforeOpening = await LatestVersionIdAsync(module);
        (await FormsAsync(id)).Single().FormModuleVersionId.Should().Be(takenBeforeOpening);

        // The open date arrives: the window opens today.
        await Context.Database.ExecuteSqlRawAsync(
            "UPDATE collections SET collection_open_date = {0} WHERE id = {1}", DateOnly.FromDateTime(DateTime.UtcNow), id);
        await PublishChangedVersionAsync(module);

        (await LatestVersionIdAsync(module)).Should().NotBe(takenBeforeOpening);
        (await FormsAsync(id)).Single().FormModuleVersionId.Should().Be(takenBeforeOpening, "its window opened before this version was published");
    }

    [Fact]
    public async Task ShouldKeepTheVersionItWasPublishedWith_WhenItOpensTheDayItIsPublished()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var module = (await ModuleIdsAsync("strategic_feedback"))[0];
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var id = await CreateAsync(Request("Opens today", saveAsDraft: false, windowOpen: false, [module]) with { OpenDate = today });
        var recorded = (await FormsAsync(id)).Single().FormModuleVersionId;
        recorded.Should().Be(await LatestVersionIdAsync(module));

        await PublishChangedVersionAsync(module);

        (await FormsAsync(id)).Single().FormModuleVersionId.Should().Be(recorded);
    }

    [Fact]
    public async Task ShouldNotMoveACollectionWhoseWindowWasMovedLater_OnceALocalAuthorityHasStartedAReport()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var module = (await ModuleIdsAsync("strategic_feedback"))[0];
        var id = await CreateAsync(Request("Started, then moved later", saveAsDraft: false, windowOpen: true, [module]));
        var original = (await FormsAsync(id)).Single().FormModuleVersionId;
        await StartReportAsync(id);
        await PublishChangedVersionAsync(module);

        // DHSC moves the open date into the future, so the collection is Scheduled again with a report under way: neither
        // that save nor the next publish may move it onto a newer version.
        await UpdateAsync(id, saveAsDraft: false, windowOpen: false, [module]);
        await PublishChangedVersionAsync(module);

        (await FormsAsync(id)).Single().FormModuleVersionId.Should().Be(original, "a started report keeps the questions it began with");
        (await FollowedAuditRowsAsync(id)).Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldRecordTheLatestVersion_ForAFormAddedToAPublishedCollection()
    {
        var modules = await ModuleIdsAsync("strategic_feedback", "activity_key_results");
        var id = await CreateAsync(Request("Form added later", saveAsDraft: false, windowOpen: false, [modules[0]]));
        var first = (await FormsAsync(id)).Single().FormModuleVersionId;

        await UpdateAsync(id, saveAsDraft: false, windowOpen: false, modules);

        var forms = await FormsAsync(id);
        forms.Single(f => f.FormModuleId == FormModuleId.From(modules[0])).FormModuleVersionId.Should().Be(first);
        forms.Single(f => f.FormModuleId == FormModuleId.From(modules[1])).FormModuleVersionId.Should().Be(await LatestVersionIdAsync(modules[1]));
    }

    [Fact]
    public async Task ShouldKeepItsVersionsWhenRevertedToDraft_AndWhenPublishedAgainWithItsWindowOpen()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var module = (await ModuleIdsAsync("strategic_feedback"))[0];
        var id = await CreateAsync(Request("Reverted", saveAsDraft: false, windowOpen: true, [module]));
        var original = (await FormsAsync(id)).Single().FormModuleVersionId;
        original.Should().NotBeNull();
        await PublishChangedVersionAsync(module);

        (await Client.PostAsync($"{Route}/{id}/revert-to-draft", null)).IsSuccessStatusCode.Should().BeTrue();
        (await FormsAsync(id)).Single().FormModuleVersionId.Should().Be(original, "only an open collection can be reverted, and its versions are fixed");

        await UpdateAsync(id, saveAsDraft: false, windowOpen: true, [module]);

        (await FormsAsync(id)).Single().FormModuleVersionId.Should().Be(original);
    }

    [Fact]
    public async Task ShouldTakeTheLatestVersion_WhenPublishedAgainWithAWindowThatHasNotOpened()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var module = (await ModuleIdsAsync("strategic_feedback"))[0];
        var id = await CreateAsync(Request("Reverted and rescheduled", saveAsDraft: false, windowOpen: true, [module]));
        var original = (await FormsAsync(id)).Single().FormModuleVersionId;
        await PublishChangedVersionAsync(module);
        (await Client.PostAsync($"{Route}/{id}/revert-to-draft", null)).IsSuccessStatusCode.Should().BeTrue();

        await UpdateAsync(id, saveAsDraft: false, windowOpen: false, [module]);

        var republished = (await FormsAsync(id)).Single().FormModuleVersionId;
        republished.Should().Be(await LatestVersionIdAsync(module));
        republished.Should().NotBe(original);
    }

    [Fact]
    public async Task ShouldKeepItsVersionsWhenRevertedToDraft_OnceALocalAuthorityHasStartedAReport()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var module = (await ModuleIdsAsync("strategic_feedback"))[0];
        var id = await CreateAsync(Request("Reverted after a report started", saveAsDraft: false, windowOpen: true, [module]));
        var original = (await FormsAsync(id)).Single().FormModuleVersionId;
        original.Should().NotBeNull();
        await StartReportAsync(id);
        await PublishChangedVersionAsync(module);

        (await Client.PostAsync($"{Route}/{id}/revert-to-draft", null)).IsSuccessStatusCode.Should().BeTrue();
        (await FormsAsync(id)).Single().FormModuleVersionId.Should().Be(original, "a started report keeps the questions it began with");

        await UpdateAsync(id, saveAsDraft: false, windowOpen: true, [module]);

        (await FormsAsync(id)).Single().FormModuleVersionId.Should().Be(original, "publishing again must not move it onto the newer version");
    }

    [Fact]
    public async Task ShouldGiveOnlyAPublishedFormItsVersionOne_WhenTheBackfillRuns()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: false);
        var draft = await CreateModuleWithAQuestionAsync("bf_never_published");
        var published = await CreateModuleWithAQuestionAsync("bf_published_unversioned");

        // With the flag off a publish saves no version, so this form is published but unversioned, like every form
        // published before BLUM-9102.
        (await Client.PostAsync($"{FormsRoute}/modules/{published}/publish", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        await RunBackfillAsync();

        (await VersionNumbersAsync(draft)).Should().BeEmpty("a draft DHSC is still building has nothing published to keep");
        (await VersionNumbersAsync(published)).Should().Equal(1);
    }

    [Fact]
    public async Task ShouldRecordNoVersion_ForMaturityCatalogueSections()
    {
        var request = Request("Maturity", saveAsDraft: false, windowOpen: false, MaturityCollectionForms.Ids) with { Type = "maturity_assessment", Quarter = null };

        var id = await CreateAsync(request);

        var forms = await FormsAsync(id);
        forms.Should().HaveCount(MaturityCollectionForms.Ids.Count);
        forms.Should().OnlyContain(f => f.FormModuleVersionId == null);
    }

    [Fact]
    public async Task ShouldLinkAPublishedCollectionsUnrecordedForm_OnceAndAuditIt_WhenTheBackfillRuns()
    {
        var module = (await ModuleIdsAsync("strategic_feedback"))[0];
        var id = await CreateAsync(Request("Before versioning", saveAsDraft: false, windowOpen: false, [module]));
        await Context.Database.ExecuteSqlRawAsync("UPDATE collection_forms SET form_module_version_id = NULL WHERE collection_id = {0}", id);
        (await FormsAsync(id)).Single().FormModuleVersionId.Should().BeNull();
        var auditsBefore = await BackfillAuditCountAsync();

        await RunBackfillAsync();

        var latest = await LatestVersionIdAsync(module);
        (await FormsAsync(id)).Single().FormModuleVersionId.Should().Be(latest);
        (await BackfillAuditCountAsync()).Should().Be(auditsBefore + 1);

        // BLUM-9084: the row lists the collection with the version each linked form now records (payload version 2).
        var row = await Context.AuditLogs.AsNoTracking()
            .Where(a => a.Action == AuditAction.FormModuleVersionsBackfilled)
            .OrderByDescending(a => a.OccurredAt)
            .FirstAsync();
        row.PayloadVersion.Should().Be(2);
        using (var after = JsonDocument.Parse(row.After!))
        {
            after.RootElement.GetProperty("collectionFormsLinked").GetInt32().Should().Be(1);
            var collection = after.RootElement.GetProperty("collections").EnumerateArray()
                .Should().ContainSingle(c => c.GetProperty("collectionId").GetGuid() == id).Subject;
            var form = collection.GetProperty("formVersions").EnumerateArray().Should().ContainSingle().Subject;
            form.GetProperty("moduleId").GetGuid().Should().Be(module);
            form.GetProperty("code").GetString().Should().Be("strategic_feedback");
            form.GetProperty("versionNumber").GetInt32().Should()
                .Be((await Context.FormModuleVersions.AsNoTracking().SingleAsync(v => v.Id == latest)).VersionNumber);
        }

        await RunBackfillAsync();

        (await BackfillAuditCountAsync()).Should().Be(auditsBefore + 1, "a second run has no gaps left to fill");
    }

    [Fact]
    public async Task ShouldListEachFormsVersionsNewestFirst_InTheFormOptions()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var module = (await ModuleIdsAsync("strategic_feedback"))[0];
        var latest = await PublishChangedVersionAsync(module);

        var options = await (await Client.GetAsync($"{Route}/form-options")).ToResponseModel<List<FormOptionResult>>();

        var versions = options.Single(o => o.ModuleId == module).Versions!.Select(v => v.VersionNumber).ToList();
        versions.First().Should().Be(latest);
        versions.Should().BeInDescendingOrder().And.Contain(1);
        options.Where(o => o.Type == "maturity_assessment").Should().OnlyContain(o => o.Versions == null || o.Versions.Count == 0);
    }

    [Fact]
    public async Task ShouldIgnoreTheFormVersionsTheClientSends()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var module = (await ModuleIdsAsync("strategic_feedback"))[0];
        await PublishChangedVersionAsync(module);
        var latest = await LatestVersionIdAsync(module);

        // The wizard still sends formVersions while BLUM_9087 is on; a version choice no longer exists.
        var id = await CreateAsync(Request("Sent a version", saveAsDraft: false, windowOpen: false, [module]) with
        {
            FormVersions = [new CollectionFormVersionRequestDto(module, 1)],
        });
        (await FormsAsync(id)).Single().FormModuleVersionId.Should().Be(latest);

        var update = await Client.PutAsJsonAsync($"{Route}/{id}", UpdateRequest(saveAsDraft: false, windowOpen: false, [module], [new CollectionFormVersionRequestDto(module, 999)]));

        update.IsSuccessStatusCode.Should().BeTrue(await update.Content.ReadAsStringAsync());
        var form = (await GetCollectionAsync(id)).Forms.Single();
        form.VersionNumber.Should().Be((await Context.FormModuleVersions.AsNoTracking().SingleAsync(v => v.Id == latest)).VersionNumber);
        form.VersionChosen.Should().BeFalse();
        (await FormsAsync(id)).Single().FormModuleVersionId.Should().Be(latest);
    }

    [Fact]
    public async Task ShouldRefuseToChangeItsForms_OnceALocalAuthorityHasStartedAReport()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var modules = await ModuleIdsAsync("strategic_feedback", "activity_key_results");
        var id = await CreateAsync(Request("Forms locked", saveAsDraft: false, windowOpen: true, modules));
        await StartReportAsync(id);
        var recorded = (await FormsAsync(id)).Select(f => (f.FormModuleId, f.FormModuleVersionId)).ToList();

        var removed = await Client.PutAsJsonAsync($"{Route}/{id}", UpdateRequest(saveAsDraft: false, windowOpen: true, [modules[0]], null));
        var unchanged = await Client.PutAsJsonAsync($"{Route}/{id}", UpdateRequest(saveAsDraft: false, windowOpen: true, modules, null));

        removed.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using (var body = JsonDocument.Parse(await removed.Content.ReadAsStringAsync()))
        {
            body.RootElement.GetProperty("error").GetString().Should().Be("COLLECTION_FORMS_LOCKED");
            body.RootElement.GetProperty("message").GetString().Should().Contain("forms cannot be changed");
        }

        unchanged.IsSuccessStatusCode.Should().BeTrue("re-sending the forms it already has changes nothing");
        (await FormsAsync(id)).Select(f => (f.FormModuleId, f.FormModuleVersionId)).Should().BeEquivalentTo(recorded);
    }

    [Fact]
    public async Task ShouldStillChangeItsForms_AfterAReportHasStarted_WhenTheFlagIsOff()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: false);
        var modules = await ModuleIdsAsync("strategic_feedback", "activity_key_results");
        var id = await CreateAsync(Request("Forms unlocked", saveAsDraft: false, windowOpen: true, [modules[0]]));
        await StartReportAsync(id);

        var added = await Client.PutAsJsonAsync($"{Route}/{id}", UpdateRequest(saveAsDraft: false, windowOpen: true, modules, null));

        added.IsSuccessStatusCode.Should().BeTrue(await added.Content.ReadAsStringAsync());
        (await FormsAsync(id)).Single(f => f.FormModuleId == FormModuleId.From(modules[1])).FormModuleVersionId
            .Should().Be(await LatestVersionIdAsync(modules[1]));
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

    private async Task<CollectionWithFormsResult> GetCollectionAsync(Guid id)
    {
        var response = await Client.GetAsync($"{Route}/{id}");
        response.IsSuccessStatusCode.Should().BeTrue();
        return await response.ToResponseModel<CollectionWithFormsResult>();
    }

    private async Task RunBackfillAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<FormModuleVersionBackfill>().RunAsync();
    }

    private Task<List<AuditLog>> FollowedAuditRowsAsync(Guid collectionId) =>
        Context.AuditLogs.AsNoTracking()
            .Where(a => a.TargetId == collectionId && a.Action == AuditAction.CollectionFormVersionsFollowed)
            .ToListAsync();

    private Task<int> BackfillAuditCountAsync() =>
        Context.AuditLogs.AsNoTracking().CountAsync(a => a.Action == AuditAction.FormModuleVersionsBackfilled);

    // Reopens the module, rewords one question and publishes, returning the new version number. The wording is unique per
    // call: switching a flag re-runs the strategic-feedback seeder, which resets labels, so a fixed suffix could recreate
    // an earlier version's content and publish no new version.
    private async Task<int> PublishChangedVersionAsync(Guid moduleId)
    {
        (await Client.PostAsync($"{FormsRoute}/modules/{moduleId}/draft", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var moduleResponse = await Client.GetAsync($"{FormsRoute}/modules/{moduleId}");
        var question = (await moduleResponse.ToResponseModel<ModuleResult>()).Questions.First();
        (await Client.PutAsJsonAsync(
            $"{FormsRoute}/modules/{moduleId}/questions/{question.Id}",
            new UpdateFormQuestionRequestDto(question.SectionId, $"{question.Label} ({Guid.NewGuid():N})", question.QuestionType, question.IsRequired, null, null, null)))
            .IsSuccessStatusCode.Should().BeTrue();

        var publish = await Client.PostAsync($"{FormsRoute}/modules/{moduleId}/publish", null);
        publish.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await publish.ToResponseModel<PublishResult>()).LatestVersionNumber!.Value;
    }

    private async Task<Guid> CreateModuleWithAQuestionAsync(string code)
    {
        var create = await Client.PostAsJsonAsync($"{FormsRoute}/modules", new CreateFormModuleRequestDto(code, $"Module {code}", null));
        create.IsSuccessStatusCode.Should().BeTrue();
        var moduleId = (await create.ToResponseModel<ModuleResult>()).Id;

        (await Client.PostAsJsonAsync(
            $"{FormsRoute}/modules/{moduleId}/questions",
            new CreateFormQuestionRequestDto(null, "Q1", "Any other notes?", "Textarea", false, null, null, null)))
            .IsSuccessStatusCode.Should().BeTrue();

        return moduleId;
    }

    private Task<List<int>> VersionNumbersAsync(Guid moduleId) =>
        Context.FormModuleVersions.AsNoTracking()
            .Where(v => v.FormModuleId == FormModuleId.From(moduleId))
            .OrderBy(v => v.VersionNumber)
            .Select(v => v.VersionNumber)
            .ToListAsync();

    private async Task<List<Guid>> ModuleIdsAsync(params string[] codes)
    {
        var modules = await Context.FormModules.AsNoTracking().Where(m => codes.Contains(m.Code)).ToListAsync();
        return codes.Select(code => modules.Single(m => m.Code == code).Id.Value).ToList();
    }

    private Task<List<CollectionForm>> FormsAsync(Guid collectionId) =>
        Context.CollectionForms.AsNoTracking().Where(f => f.CollectionId == new CollectionId(collectionId)).ToListAsync();

    private async Task<FormModuleVersionId?> LatestVersionIdAsync(FormModuleId moduleId) =>
        (await Context.FormModuleVersions.AsNoTracking()
            .Where(v => v.FormModuleId == moduleId)
            .OrderByDescending(v => v.VersionNumber)
            .FirstAsync()).Id;

    private Task<FormModuleVersionId?> LatestVersionIdAsync(Guid moduleId) => LatestVersionIdAsync(FormModuleId.From(moduleId));

    private async Task<Guid> CreateAsync(CreateCollectionRequestDto request)
    {
        var response = await Client.PostAsJsonAsync(Route, request);
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        return (await response.ToResponseModel<CollectionResult>()).Id;
    }

    private async Task UpdateAsync(Guid id, bool saveAsDraft, bool windowOpen, IReadOnlyList<Guid> modules, IReadOnlyList<CollectionFormVersionRequestDto>? versions = null)
    {
        var response = await Client.PutAsJsonAsync($"{Route}/{id}", UpdateRequest(saveAsDraft, windowOpen, modules, versions));
        response.IsSuccessStatusCode.Should().BeTrue();
    }

    private static UpdateCollectionRequestDto UpdateRequest(bool saveAsDraft, bool windowOpen, IReadOnlyList<Guid> modules, IReadOnlyList<CollectionFormVersionRequestDto>? versions)
    {
        var (open, deadline) = Window(windowOpen);
        return new UpdateCollectionRequestDto(
            Name: "Updated",
            Purpose: null,
            Type: "quarterly_report",
            FinancialYear: "2026/27",
            Quarter: 1,
            WindowSameAsReportingPeriod: false,
            CollectionOpenDate: open,
            SubmissionDeadline: deadline,
            IsSubmittedByAllLas: true,
            SaveAsDraft: saveAsDraft,
            FormModuleIds: modules,
            LocalAuthorityIds: null,
            Reminders: null,
            FormVersions: versions);
    }

    private static CreateCollectionRequestDto Request(string name, bool saveAsDraft, bool windowOpen, IReadOnlyList<Guid> modules)
    {
        var (open, deadline) = Window(windowOpen);
        return new CreateCollectionRequestDto(
            Name: name,
            Purpose: null,
            Type: "quarterly_report",
            FinancialYear: "2026/27",
            Quarter: 1,
            WindowSameAsReportingPeriod: false,
            OpenDate: open,
            Deadline: deadline,
            IsSubmittedByAllLas: true,
            SaveAsDraft: saveAsDraft,
            LocalAuthorityIds: null,
            FormModuleIds: modules,
            Reminders: null);
    }

    // An open window (revert to draft needs one), or one that has not started yet.
    private static (DateOnly Open, DateOnly Deadline) Window(bool open)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return open ? (today.AddDays(-5), today.AddDays(25)) : (today.AddDays(5), today.AddDays(35));
    }
}