using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Dtos;
using Application.Common.Features;
using Domain.AuditLogs;
using Domain.Collections;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.QuarterlyReports;

// BLUM-9106: which form version a collection's reports used is visible in the DHSC export and the audit log, and a
// report with no saved copy is rebuilt from its collection's version rather than the live form. Every test sets the
// flag on its first line: the settings singleton outlives a test.
public class FormVersionExportAuditTests(IntegrationTestWebFactory factory) : QuarterlyReportTestBase(factory)
{
    private const string FormsRoute = "admin/data-collection-forms";
    private const string OtherPreventionCode = "SF_OTHER_PREVENTION";
    private const string Reworded = " (version 2)";

    private sealed record FormQuestionResult(Guid Id, Guid? SectionId, string Code, string Label, string QuestionType, bool IsRequired, string? Hint, string? HelpText);

    private sealed record FormModuleResult(Guid Id, List<FormQuestionResult> Questions);

    [Fact]
    public async Task ShouldShowEachReportsFormVersions_InSummaryCsv()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var collectionId = await CreateOpenQuarterCollectionAsync(1, "Q1 summary versions");
        AuthenticateAsAdmin();

        var lines = (await ReadZipEntryAsync(await Client.GetAsync($"data-collections/{collectionId}/submissions/export"), "summary.csv"))
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        lines[0].Should().EndWith(",Form versions");
        var expected = await ExpectedVersionsLabelAsync(collectionId);
        expected.Should().Contain("Provide strategic feedback: version ");
        lines.Skip(1).Should().NotBeEmpty().And.OnlyContain(line => line.EndsWith(expected) || line.EndsWith($"\"{expected}\""));
    }

    // BLUM-9084: each form is named as its recorded version names it, so one archived or renamed since is still named
    // as the reports knew it, never by its id or its new live name.
    [Fact]
    public async Task ShouldNameEachFormFromItsVersion_WhenTheFormIsArchivedOrRenamed()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var collectionId = await CreateOpenQuarterCollectionAsync(1, "Q1 archived form versions");
        var expected = await ExpectedVersionsLabelAsync(collectionId);
        var feedback = await Context.FormModules.AsNoTracking().SingleAsync(m => m.Code == FeedbackModuleCode);
        AuthenticateAsAdmin();

        await Context.Database.ExecuteSqlAsync($"UPDATE form_modules SET name = 'Renamed since', is_active = false WHERE id = {feedback.Id.Value}");
        try
        {
            var lines = (await ReadZipEntryAsync(await Client.GetAsync($"data-collections/{collectionId}/submissions/export"), "summary.csv"))
                .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

            expected.Should().Contain($"{feedback.Name}: version ");
            lines.Skip(1).Should().NotBeEmpty().And.OnlyContain(line => line.EndsWith(expected) || line.EndsWith($"\"{expected}\""));
            lines.Should().NotContain(line => line.Contains("Renamed since") || line.Contains(feedback.Id.Value.ToString()));
        }
        finally
        {
            await Context.Database.ExecuteSqlAsync($"UPDATE form_modules SET name = {feedback.Name}, is_active = true WHERE id = {feedback.Id.Value}");
        }
    }

    [Fact]
    public async Task ShouldLeaveSummaryCsvUnchanged_WhenTheFlagIsOff()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: false);
        var collectionId = await CreateOpenQuarterCollectionAsync(1, "Q1 summary flag off");
        AuthenticateAsAdmin();

        var header = (await ReadZipEntryAsync(await Client.GetAsync($"data-collections/{collectionId}/submissions/export"), "summary.csv")).Split("\r\n")[0];

        header.Should().NotContain("Form versions");
    }

    [Fact]
    public async Task ShouldRecordEachFormsVersion_InTheAuditRowWhenACollectionTakesItsVersions()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var collectionId = await CreateOpenQuarterCollectionAsync(1, "Q1 audited versions");

        var row = await Context.AuditLogs.AsNoTracking().SingleAsync(a => a.TargetId == collectionId && a.Action == AuditAction.CollectionCreated);

        row.PayloadVersion.Should().Be(2);
        using var after = JsonDocument.Parse(row.After!);
        var formVersions = after.RootElement.GetProperty("formVersions").EnumerateArray().ToList();
        var recorded = await Context.CollectionForms.AsNoTracking().Where(f => f.CollectionId == new CollectionId(collectionId)).ToListAsync();
        formVersions.Should().HaveCount(recorded.Count);
        foreach (var form in recorded.Where(f => f.FormModuleVersionId is not null))
        {
            var number = (await Context.FormModuleVersions.AsNoTracking().SingleAsync(v => v.Id == form.FormModuleVersionId)).VersionNumber;
            formVersions.Should().Contain(entry =>
                entry.GetProperty("moduleId").GetGuid() == form.FormModuleId.Value && entry.GetProperty("versionNumber").GetInt32() == number);
        }
    }

    [Fact]
    public async Task ShouldWriteTheVersionOneAuditShape_WhenTheFlagIsOff()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: false);
        var collectionId = await CreateOpenQuarterCollectionAsync(1, "Q1 audit flag off");

        var row = await Context.AuditLogs.AsNoTracking().SingleAsync(a => a.TargetId == collectionId && a.Action == AuditAction.CollectionCreated);

        row.PayloadVersion.Should().Be(AuditLog.InitialPayloadVersion);
        row.After.Should().NotContain("formVersions");
    }

    [Fact]
    public async Task ShouldRebuildAReportWithNoSavedCopy_FromItsCollectionsVersion()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        AuthenticateAsAdmin();
        await PublishLiveAsync("strategic_feedback");
        var collectionId = await CreateOpenQuarterCollectionAsync(1, "Q1 rebuilt from version");

        AuthenticateAsMainOrgLead();
        var activity = await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        await CompleteActivityReportAsync(collectionId, activity.Id);
        await SubmitStartedReportAsync(collectionId);
        var label = (await GetStrategicFeedbackAsync(collectionId)).Pages.SelectMany(p => p.Questions).Single(q => q.Code == OtherPreventionCode).Label;

        // An older report: submitted before the copy was saved. Then DHSC rewords the question in a new version.
        await Context.Database.ExecuteSqlRawAsync("UPDATE submissions SET report_snapshot_json = NULL WHERE collection_id = {0}", collectionId);
        AuthenticateAsAdmin();
        await PublishRewordedAsync("strategic_feedback", OtherPreventionCode);

        AuthenticateAsMainOrgLead();
        var feedbackCsv = await ReadZipEntryAsync(await Client.GetAsync($"quarterly-report/{collectionId}/export"), "strategic-feedback.csv");

        feedbackCsv.Should().Contain(label);
        feedbackCsv.Should().NotContain(label + Reworded, "the report is rebuilt from its collection's version, not the live form");
        (await Context.Submissions.AsNoTracking().SingleAsync(s => s.CollectionId == new CollectionId(collectionId))).ReportSnapshotJson.Should().BeNull();
    }

    private async Task<string> ExpectedVersionsLabelAsync(Guid collectionId)
    {
        var recorded = await Context.CollectionForms.AsNoTracking()
            .Where(f => f.CollectionId == new CollectionId(collectionId) && f.FormModuleVersionId != null)
            .ToListAsync();
        var modules = await Context.FormModules.AsNoTracking().ToListAsync();
        var parts = new List<(int Order, string Text)>();
        foreach (var form in recorded)
        {
            var module = modules.Single(m => m.Id == form.FormModuleId);
            var number = (await Context.FormModuleVersions.AsNoTracking().SingleAsync(v => v.Id == form.FormModuleVersionId)).VersionNumber;
            parts.Add((module.DisplayOrder, $"{module.Name}: version {number}"));
        }

        return string.Join("; ", parts.OrderBy(p => p.Order).Select(p => p.Text));
    }

    // Publishes the live form unchanged, so the latest version matches it before the test records one.
    private async Task PublishLiveAsync(string moduleCode)
    {
        var module = await Context.FormModules.AsNoTracking().SingleAsync(m => m.Code == moduleCode);
        (await Client.PostAsync($"{FormsRoute}/modules/{module.Id.Value}/draft", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client.PostAsync($"{FormsRoute}/modules/{module.Id.Value}/publish", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task PublishRewordedAsync(string moduleCode, string questionCode)
    {
        var module = await Context.FormModules.AsNoTracking().SingleAsync(m => m.Code == moduleCode);
        (await Client.PostAsync($"{FormsRoute}/modules/{module.Id.Value}/draft", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var question = (await (await Client.GetAsync($"{FormsRoute}/modules/{module.Id.Value}")).ToResponseModel<FormModuleResult>())
            .Questions.Single(q => q.Code == questionCode);
        (await Client.PutAsJsonAsync(
            $"{FormsRoute}/modules/{module.Id.Value}/questions/{question.Id}",
            new UpdateFormQuestionRequestDto(question.SectionId, question.Label + Reworded, question.QuestionType, question.IsRequired, question.Hint, question.HelpText, null)))
            .IsSuccessStatusCode.Should().BeTrue();
        (await Client.PostAsync($"{FormsRoute}/modules/{module.Id.Value}/publish", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<string> ReadZipEntryAsync(HttpResponseMessage response, string entryName)
    {
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var entry = archive.GetEntry(entryName);
        entry.Should().NotBeNull("the export ZIP should contain {0}", entryName);
        using var reader = new StreamReader(entry!.Open());
        return await reader.ReadToEndAsync();
    }
}