using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Dtos;
using Api.Tests.Integration.QuarterlyReports;
using Application.Common.Features;
using Domain.AuditLogs;
using Domain.Collections;
using Domain.DataCollectionForms;
using Domain.Submissions;
using FluentAssertions;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Seed.FormVersions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Collections;

// BLUM-9084: "have reports started in this collection?" stays true until the change that asked it commits. A change to
// a collection's forms or recorded versions checks it under the collection's row lock (FOR NO KEY UPDATE), and starting
// a report reads the collection under the same row's FOR SHARE lock, so the two wait for each other (CollectionRowLocks).
//
// Each test holds the other side's lock in a transaction of its own, on a second connection, fires the request, waits
// until that request is blocked on a lock (or has already finished, which is what happens without the lock), and only
// then commits the other side. Without the locks every one of these requests completes against the state it read
// first, and its assertion fails.
public class CollectionRowLockTests(IntegrationTestWebFactory factory) : QuarterlyReportTestBase(factory)
{
    private const string FormsRoute = "admin/data-collection-forms";

    private sealed record QuestionResult(Guid Id, Guid? SectionId, string Code, string Label, string QuestionType, bool IsRequired);

    private sealed record ModuleResult(Guid Id, List<QuestionResult> Questions);

    [Fact]
    public async Task ShouldRefuseAFormsChange_ThatWaitedForAReportBeingStarted()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var modules = await ModuleIdsAsync(FeedbackModuleCode, KeyResultsModuleCode);
        var (open, deadline) = (Today.AddDays(-1), Today.AddDays(60));
        var id = await CreateQuarterlyCollectionAsync("Forms change during a report start", open, deadline, formModuleIds: modules);

        await using (var reportStart = await OtherSideAsync(id, "FOR SHARE"))
        {
            await reportStart.Context.Submissions.AddAsync(StartedReport(id));
            await reportStart.Context.SaveChangesAsync();

            var pending = Client.PutAsJsonAsync($"data-collections/{id}", UpdateRequest(open, deadline, [modules[0]]));
            await UntilBlockedOrDoneAsync(pending);
            await reportStart.Transaction.CommitAsync();

            var response = await pending;

            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await response.Content.ReadAsStringAsync());
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            body.RootElement.GetProperty("error").GetString().Should().Be("COLLECTION_FORMS_LOCKED");
        }

        (await FormModuleIdsOfAsync(id)).Should().BeEquivalentTo(modules, "the report that started first keeps its forms");
    }

    [Fact]
    public async Task ShouldStartAReportOnTheChangedForms_WhenItWaitedForAFormsChange()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var modules = await ModuleIdsAsync(FeedbackModuleCode, KeyResultsModuleCode);
        var id = await CreateQuarterlyCollectionAsync("Report start during a forms change", Today.AddDays(-1), Today.AddDays(60), formModuleIds: modules);

        await using (var formsChange = await OtherSideAsync(id, "FOR NO KEY UPDATE"))
        {
            await formsChange.Context.Database.ExecuteSqlAsync(
                $"DELETE FROM collection_forms WHERE collection_id = {id} AND form_module_id = {modules[1]}");

            AuthenticateAsMainOrgLead();
            var pending = Client.PostAsync($"quarterly-report/{id}/submission", null);
            await UntilBlockedOrDoneAsync(pending);
            await formsChange.Transaction.CommitAsync();

            var response = await pending;
            response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        }

        var submission = await Context.Submissions.AsNoTracking()
            .Include(s => s.Sections)
            .SingleAsync(s => s.CollectionId == new CollectionId(id));
        submission.Sections.Select(s => s.FormModuleId.Value).Should().Equal([modules[0]], "the report reads the forms the change left");
    }

    [Fact]
    public async Task ShouldNotMoveAScheduledCollection_WhoseReportStartedWhileTheFormWasPublished()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var module = (await ModuleIdsAsync(FeedbackModuleCode))[0];
        var id = await CreateQuarterlyCollectionAsync("Publish during a report start", Today.AddDays(5), Today.AddDays(60), formModuleIds: [module]);
        var recorded = await RecordedVersionAsync(id);
        await RewordInADraftAsync(module);

        await using (var reportStart = await OtherSideAsync(id, "FOR SHARE"))
        {
            await reportStart.Context.Submissions.AddAsync(StartedReport(id));
            await reportStart.Context.SaveChangesAsync();

            var pending = Client.PostAsync($"{FormsRoute}/modules/{module}/publish", null);
            await UntilBlockedOrDoneAsync(pending);
            await reportStart.Transaction.CommitAsync();

            (await pending).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        (await LatestVersionIdAsync(module)).Should().NotBe(recorded, "the publish saved a new version");
        (await RecordedVersionAsync(id)).Should().Be(recorded, "a collection whose report has started keeps its version");
        (await FollowedAuditRowsAsync(id)).Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldNotMoveAScheduledCollection_WhoseReportStartedWhileTheBootSyncRan()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var module = (await ModuleIdsAsync(FeedbackModuleCode))[0];
        var id = await CreateQuarterlyCollectionAsync("Boot sync during a report start", Today.AddDays(5), Today.AddDays(60), formModuleIds: [module]);
        var older = await RecordedVersionAsync(id);
        await RewordInADraftAsync(module);
        (await Client.PostAsync($"{FormsRoute}/modules/{module}/publish", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Behind the latest version, as a collection is after a release the sync has yet to catch it up on.
        await Context.Database.ExecuteSqlAsync(
            $"UPDATE collection_forms SET form_module_version_id = {older!.Value} WHERE collection_id = {id}");
        await Context.Database.ExecuteSqlAsync($"DELETE FROM audit_logs WHERE target_id = {id}");

        await using (var reportStart = await OtherSideAsync(id, "FOR SHARE"))
        {
            await reportStart.Context.Submissions.AddAsync(StartedReport(id));
            await reportStart.Context.SaveChangesAsync();

            var pending = Task.Run(async () =>
            {
                await using var scope = factory.Services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<FormModuleVersionSync>().RunAsync();
            });
            await UntilBlockedOrDoneAsync(pending);
            await reportStart.Transaction.CommitAsync();

            await pending;
        }

        (await LatestVersionIdAsync(module)).Should().NotBe(older);
        (await RecordedVersionAsync(id)).Should().Be(older, "a collection whose report has started keeps its version");
        (await FollowedAuditRowsAsync(id)).Should().BeEmpty();
    }

    // The other side of the race: a second connection holding a lock on the collection row in an open transaction, as a
    // report start (FOR SHARE) or a forms change (FOR NO KEY UPDATE) does. Disposing it rolls back whatever is uncommitted.
    private async Task<OtherSide> OtherSideAsync(Guid collectionId, string lockClause)
    {
        var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var transaction = await context.Database.BeginTransactionAsync();
#pragma warning disable EF1002 // The lock clause is one of two literals in this file, never input.
        await context.Database.ExecuteSqlRawAsync($"SELECT 1 FROM collections WHERE id = {{0}} {lockClause}", collectionId);
#pragma warning restore EF1002
        return new OtherSide(scope, context, transaction);
    }

    // Waits until some backend on this class's database is blocked on a lock, which is the request under test once it
    // reaches the row the other side holds, or until the request has finished without waiting.
    private async Task UntilBlockedOrDoneAsync(Task pending)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!pending.IsCompleted && DateTime.UtcNow < deadline)
        {
            var waiting = await Context.Database
                .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'")
                .SingleAsync();
            if (waiting > 0)
            {
                return;
            }

            await Task.Delay(20);
        }
    }

    private static Submission StartedReport(Guid collectionId) =>
        Submission.Start(
            SubmissionId.New(),
            new CollectionId(collectionId),
            MainOrg.Id,
            Array.Empty<(FormModuleId ModuleId, string ModuleCode)>(),
            AdminsData.MainAdmin.Id,
            DateTime.UtcNow);

    private UpdateCollectionRequestDto UpdateRequest(DateOnly open, DateOnly deadline, IReadOnlyList<Guid> modules) =>
        new(
            Name: "Forms change during a report start",
            Purpose: "Quarterly report tests",
            Type: "quarterly_report",
            FinancialYear: TestFinancialYears.Current,
            Quarter: 1,
            WindowSameAsReportingPeriod: false,
            CollectionOpenDate: open,
            SubmissionDeadline: deadline,
            IsSubmittedByAllLas: false,
            SaveAsDraft: false,
            FormModuleIds: modules,
            LocalAuthorityIds: [MainOrg.Id.Value]);

    // Reopens the module and rewords its first question, so the publish that follows saves a new version. The wording is
    // unique per call, so it never matches an earlier version's content.
    private async Task RewordInADraftAsync(Guid moduleId)
    {
        AuthenticateAsAdmin();
        (await Client.PostAsync($"{FormsRoute}/modules/{moduleId}/draft", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var question = (await (await Client.GetAsync($"{FormsRoute}/modules/{moduleId}")).ToResponseModel<ModuleResult>()).Questions.First();
        (await Client.PutAsJsonAsync(
            $"{FormsRoute}/modules/{moduleId}/questions/{question.Id}",
            new UpdateFormQuestionRequestDto(question.SectionId, $"{question.Label} ({Guid.NewGuid():N})", question.QuestionType, question.IsRequired, null, null, null)))
            .IsSuccessStatusCode.Should().BeTrue();
    }

    private async Task<List<Guid>> ModuleIdsAsync(params string[] codes)
    {
        var modules = await Context.FormModules.AsNoTracking().Where(m => codes.Contains(m.Code)).ToListAsync();
        return codes.Select(code => modules.Single(m => m.Code == code).Id.Value).ToList();
    }

    private Task<List<Guid>> FormModuleIdsOfAsync(Guid collectionId) =>
        Context.CollectionForms.AsNoTracking()
            .Where(f => f.CollectionId == new CollectionId(collectionId))
            .Select(f => f.FormModuleId.Value)
            .ToListAsync();

    private async Task<FormModuleVersionId?> RecordedVersionAsync(Guid collectionId) =>
        (await Context.CollectionForms.AsNoTracking().SingleAsync(f => f.CollectionId == new CollectionId(collectionId))).FormModuleVersionId;

    private async Task<FormModuleVersionId?> LatestVersionIdAsync(Guid moduleId) =>
        (await Context.FormModuleVersions.AsNoTracking()
            .Where(v => v.FormModuleId == FormModuleId.From(moduleId))
            .OrderByDescending(v => v.VersionNumber)
            .FirstAsync()).Id;

    private Task<List<AuditLog>> FollowedAuditRowsAsync(Guid collectionId) =>
        Context.AuditLogs.AsNoTracking()
            .Where(a => a.TargetId == collectionId && a.Action == AuditAction.CollectionFormVersionsFollowed)
            .ToListAsync();

    private sealed record OtherSide(AsyncServiceScope Scope, ApplicationDbContext Context, IDbContextTransaction Transaction) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Transaction.DisposeAsync();
            await Scope.DisposeAsync();
        }
    }
}