using System.IO.Compression;
using System.Net.Http.Json;
using Api.Dtos;
using Api.Tests.Integration.QuarterlyReports;
using Application.MaturityAssessments.Protocol;
using Application.MaturityAssessments.Snapshots;
using Domain.Collections;
using Domain.MaturityAssessments;
using Domain.Organisations;
using Domain.Users;
using Domain.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Tests.Data;

namespace Api.Tests.Integration.MaturityAssessments;

// Shared setup for the annual maturity assessment (BLUM-7855 / 7856 / 7857 / 7858 / 7859). Builds on the quarterly
// base for its organisations, collections and quarterly-report helpers (the quarterly report is one of the pre-fill
// sources), and adds the maturity-assessment API surface plus the seeding the read-only and DHSC screens need.
public abstract class MaturityAssessmentTestBase(IntegrationTestWebFactory factory) : QuarterlyReportTestBase(factory)
{
    protected const string MainOrgLeadEmail = "maturity.lead@example.gov.uk";

    protected const string RegionalNetworkTaskName = "Joining and attending a regional network";

    // A one-row funded-roles repeater every "complete the section" walk uses for MA_FUNDED_ROLES.
    protected static readonly string RolesJson = MaturityRolesAnswer.Serialize(
        [new MaturityRolesAnswer.Entry("public_health_officer", "81_100", null)]);

    private sealed record CollectionResult(Guid Id, string Name, string Status, bool IsDraft);

    // --- collections ---

    // A published maturity-assessment collection with every knob the tests need (the quarterly base's helper has
    // no all-LAs flag). Null localAuthorityIds with isSubmittedByAllLas false defaults to MainOrg; null formIds
    // selects every section, as the Forms step preselects them.
    protected async Task<Guid> CreateMaturityCollectionAsync(
        string name = "Maturity assessment",
        DateOnly? openDate = null,
        DateOnly? deadline = null,
        IReadOnlyList<Guid>? localAuthorityIds = null,
        bool isSubmittedByAllLas = false,
        string? financialYear = null,
        bool saveAsDraft = false,
        IReadOnlyList<Guid>? formIds = null)
    {
        AuthenticateAsAdmin();

        var request = new CreateCollectionRequestDto(
            Name: name,
            Purpose: "Maturity assessment tests",
            Type: "maturity_assessment",
            FinancialYear: financialYear ?? TestFinancialYears.Current,
            Quarter: null,
            WindowSameAsReportingPeriod: false,
            OpenDate: saveAsDraft ? null : openDate ?? Today.AddDays(-1),
            Deadline: saveAsDraft ? null : deadline ?? Today.AddDays(60),
            IsSubmittedByAllLas: isSubmittedByAllLas,
            SaveAsDraft: saveAsDraft,
            LocalAuthorityIds: isSubmittedByAllLas ? [] : localAuthorityIds ?? [MainOrg.Id.Value],
            FormModuleIds: formIds ?? MaturityCollectionForms.Ids,
            Reminders: null);

        var response = await Client.PostAsJsonAsync("data-collections", request);
        response.IsSuccessStatusCode.Should().BeTrue(
            "creating maturity assessment \"{0}\" failed: {1}", name, await response.Content.ReadAsStringAsync());
        return (await response.ToResponseModel<CollectionResult>()).Id;
    }

    // --- the LA-facing surface ---

    protected Task<HttpResponseMessage> GetContextAsync(Guid collectionId) =>
        Client.GetAsync($"maturity-assessment/{collectionId}/context");

    protected Task<HttpResponseMessage> StartAsync(Guid collectionId) =>
        Client.PostAsync($"maturity-assessment/{collectionId}/assessment", null);

    protected async Task<MaturityTaskListDto> StartAssessmentAsync(Guid collectionId)
    {
        var response = await StartAsync(collectionId);
        response.IsSuccessStatusCode.Should().BeTrue("starting the assessment failed: {0}", await response.Content.ReadAsStringAsync());
        return await response.ToResponseModel<MaturityTaskListDto>();
    }

    protected Task<HttpResponseMessage> GetMaturityTaskListAsync(Guid collectionId) =>
        Client.GetAsync($"maturity-assessment/{collectionId}/task-list");

    protected Task<HttpResponseMessage> GetSectionResponseAsync(Guid collectionId, string sectionCode) =>
        Client.GetAsync($"maturity-assessment/{collectionId}/sections/{sectionCode}");

    protected async Task<MaturitySectionDto> GetSectionAsync(Guid collectionId, string sectionCode)
    {
        var response = await GetSectionResponseAsync(collectionId, sectionCode);
        response.IsSuccessStatusCode.Should().BeTrue("reading section {0} failed: {1}", sectionCode, await response.Content.ReadAsStringAsync());
        return await response.ToResponseModel<MaturitySectionDto>();
    }

    protected Task<HttpResponseMessage> SavePageAsync(
        Guid collectionId,
        string sectionCode,
        string pageCode,
        bool saveAsDraft,
        params (string Code, string? Value)[] answers)
    {
        var request = new SaveMaturityAnswersRequestDto(
            pageCode,
            saveAsDraft,
            answers.Select(a => new MaturityAnswerInputDto(a.Code, a.Value)).ToList());
        return Client.PutAsJsonAsync($"maturity-assessment/{collectionId}/sections/{sectionCode}/answers", request);
    }

    protected Task<HttpResponseMessage> CompleteSectionAsync(Guid collectionId, string sectionCode) =>
        Client.PostAsync($"maturity-assessment/{collectionId}/sections/{sectionCode}/complete", null);

    protected Task<HttpResponseMessage> SubmitAssessmentAsync(Guid collectionId) =>
        Client.PostAsync($"maturity-assessment/{collectionId}/submit", null);

    protected Task<HttpResponseMessage> GetSubmittedAsync(Guid collectionId) =>
        Client.GetAsync($"maturity-assessment/{collectionId}/submitted");

    protected Task<HttpResponseMessage> ExportAssessmentAsync(Guid collectionId) =>
        Client.GetAsync($"maturity-assessment/{collectionId}/export");

    // Answers every required, visible question on every visible page of a section the way the frontend does —
    // page by page as draft saves — then completes it through check-your-answers. Overrides pin specific answers
    // (e.g. MA_NA_PUBLISHED = no to hide the follow-on pages); everything else takes the first option, a short text,
    // or the one-row repeater.
    protected async Task CompleteSectionFullyAsync(Guid collectionId, string sectionCode, params (string Code, string Value)[] overrides)
    {
        var section = await GetSectionAsync(collectionId, sectionCode);
        var pinned = overrides.ToDictionary(o => o.Code, o => o.Value, StringComparer.Ordinal);
        var given = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var page in section.Pages)
        {
            if (page.VisibleWhen is { } rule
                && !(given.TryGetValue(rule.QuestionCode, out var parent) && rule.Values.Contains(parent)))
            {
                continue;
            }

            var answers = new List<(string Code, string? Value)>();
            foreach (var question in page.Questions.OrderBy(q => q.DisplayOrder))
            {
                if (!question.IsRequired || !IsQuestionVisible(question, given))
                {
                    continue;
                }

                var value = pinned.TryGetValue(question.Code, out var forced) ? forced : AnswerValueFor(question);
                given[question.Code] = value;
                answers.Add((question.Code, value));
            }

            if (answers.Count == 0)
            {
                continue;
            }

            var save = await SavePageAsync(collectionId, sectionCode, page.Code, saveAsDraft: true, [.. answers]);
            save.IsSuccessStatusCode.Should().BeTrue(
                "saving page {0} of {1} failed: {2}", page.Code, sectionCode, await save.Content.ReadAsStringAsync());
        }

        var complete = await CompleteSectionAsync(collectionId, sectionCode);
        complete.IsSuccessStatusCode.Should().BeTrue(
            "completing section {0} failed: {1}", sectionCode, await complete.Content.ReadAsStringAsync());
    }

    protected async Task CompleteAllSectionsAsync(Guid collectionId, params (string Code, string Value)[] overrides)
    {
        foreach (var sectionCode in MaturityProtocol.SectionCodes)
        {
            await CompleteSectionFullyAsync(collectionId, sectionCode, overrides);
        }
    }

    private static string AnswerValueFor(MaturityQuestionDto question)
    {
        if (question.QuestionType == MaturityFieldTypes.Roles)
        {
            return RolesJson;
        }

        return question.Options.Count > 0 ? question.Options[0].Value : "Answer given in the assessment";
    }

    // Port of SubmissionAnswerValidator.IsVisible — both sides comma-separated allow-lists.
    private static bool IsQuestionVisible(MaturityQuestionDto question, IReadOnlyDictionary<string, string> answers)
    {
        if (string.IsNullOrWhiteSpace(question.ConditionalQuestionCode) || string.IsNullOrWhiteSpace(question.ConditionalValue))
        {
            return true;
        }

        if (!answers.TryGetValue(question.ConditionalQuestionCode, out var parent) || string.IsNullOrWhiteSpace(parent))
        {
            return false;
        }

        var allowed = question.ConditionalValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parent.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(allowed.Contains);
    }

    // --- the quarterly pre-fill source ---

    // Reports an Active regional-network activity through a quarterly report, answering RNW_MEMBERSHIP explicitly
    // (the generic completion helper picks the first option, which would make a "yes" assertion vacuous).
    protected async Task<Guid> SubmitRegionalNetworkReportAsync(Guid quarterlyId, string membership) =>
        await SaveRegionalNetworkReportAsync(quarterlyId, membership, submit: true);

    protected async Task<Guid> SaveRegionalNetworkReportAsync(Guid quarterlyId, string membership, bool submit)
    {
        var (_, taskId) = await GetTypeAsync(RegionalNetworkTaskName);
        var results = await StandardResultsAsync(taskId, "rnw_membership", "rnw_representative");
        var activity = await CreateCompletedActivityAsync(RegionalNetworkTaskName, "active", results);

        await CreateSubmissionAsync(quarterlyId);
        await ConfirmActivitiesAsync(quarterlyId);

        var questions = await GetActivityQuestionsAsync(quarterlyId, activity.Id);
        var answers = questions.Questions
            .Where(q => q.IsRequired && string.IsNullOrWhiteSpace(q.ConditionalQuestionCode))
            .Select(q => (q.Code, (string?)(q.Code == "RNW_MEMBERSHIP" ? membership : q.Options.FirstOrDefault()?.Value ?? "Progress made this quarter")))
            .ToArray();
        (await SaveAnswersAsync(quarterlyId, activity.Id, saveAsDraft: true, answers)).EnsureSuccessStatusCode();

        if (!submit)
        {
            return activity.Id;
        }

        (await CompleteActivityAsync(quarterlyId, activity.Id)).EnsureSuccessStatusCode();
        await SubmitStartedReportAsync(quarterlyId);
        return activity.Id;
    }

    // --- seeding ---

    // The 2025 survey baseline for an organisation, as the boot seeder would have written it.
    protected async Task SeedBaselineAnswersAsync(Organisation organisation, params (string Code, string Value)[] answers)
    {
        foreach (var (code, value) in answers)
        {
            await Context.MaturityBaselineAnswers.AddAsync(
                MaturityBaselineAnswer.Import(MaturityBaselineAnswerId.New(), organisation.Id, code, value, 2025, DateTime.UtcNow));
        }

        await SaveChangesAsync();
    }

    // A finalised assessment written directly — enough for the history, DHSC and pre-fill reads without driving the
    // whole ten-section flow. Answers become the assessment's stored rows (the prior-assessment pre-fill source).
    protected async Task<MaturityAssessment> SeedSubmittedAssessmentAsync(
        Guid collectionId,
        Organisation organisation,
        string referenceNumber,
        string? snapshotJson = null,
        User? submitter = null,
        params (string Code, string Value)[] answers)
    {
        var actor = submitter?.Id ?? AdminsData.MainAdmin.Id;
        var assessment = MaturityAssessment.Start(
            MaturityAssessmentId.New(),
            new CollectionId(collectionId),
            organisation.Id,
            MaturityProtocol.SectionCodes,
            actor,
            DateTime.UtcNow);

        foreach (var code in MaturityProtocol.SectionCodes)
        {
            assessment.SetSectionStatus(code, MaturityAssessmentSectionStatus.Completed, actor, DateTime.UtcNow);
        }

        assessment.Submit(referenceNumber, actor, DateTime.UtcNow);
        assessment.AttachSnapshot(snapshotJson ?? MaturityAssessmentSnapshotJson.Serialize(new MaturityAssessmentSnapshotDocument([])));
        await Context.MaturityAssessments.AddAsync(assessment);

        foreach (var (code, value) in answers)
        {
            await Context.MaturityAssessmentAnswers.AddAsync(
                MaturityAssessmentAnswer.Record(MaturityAssessmentAnswerId.New(), assessment.Id, code, value, AnswerSource.User, actor, DateTime.UtcNow));
        }

        await SaveChangesAsync();
        return assessment;
    }

    protected async Task<User> SeedSubmitterAsync(string firstName = "Julie", string lastName = "Smith")
    {
        var submitter = User.New(
            UserId.New(),
            new Name(firstName, lastName),
            $"{firstName}.{lastName}.{Guid.NewGuid():N}@example.gov.uk".ToLowerInvariant(),
            new SubId(Guid.NewGuid()),
            UserStatus.Active,
            UserRole.Lead,
            DateTime.UtcNow);
        await Context.Users.AddAsync(submitter);
        await SaveChangesAsync();
        return submitter;
    }

    // The submit confirmation is addressed from a real users row (as in production), so seed one for the acting
    // Lead identity.
    protected async Task EnsureMainOrgLeadUserAsync()
    {
        var leadId = new UserId(MainOrgLeadUserId);
        if (await Context.Users.AsNoTracking().AnyAsync(u => u.Id == leadId))
        {
            return;
        }

        await Context.Users.AddAsync(User.New(leadId, new Name("Maturity", "Lead"), MainOrgLeadEmail, new SubId(Guid.NewGuid()), UserStatus.Active, UserRole.Lead, DateTime.UtcNow));
        await SaveChangesAsync();
    }

    protected async Task<MaturityAssessment> GetAssessmentAsync(Guid collectionId, Organisation? organisation = null)
    {
        var organisationId = (organisation ?? MainOrg).Id;
        return await Context.MaturityAssessments
            .AsNoTracking()
            .Include(a => a.Sections)
            .FirstAsync(a => a.CollectionId == new CollectionId(collectionId) && a.OrganisationId == organisationId);
    }

    protected async Task<IReadOnlyList<MaturityAssessmentAnswer>> GetAnswersAsync(Guid collectionId, Organisation? organisation = null)
    {
        var assessment = await GetAssessmentAsync(collectionId, organisation);
        return await Context.MaturityAssessmentAnswers
            .AsNoTracking()
            .Where(a => a.AssessmentId == assessment.Id)
            .ToListAsync();
    }

    // --- ZIPs ---

    protected static async Task<string> ReadZipEntryAsync(HttpResponseMessage response, string entryName)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var entry = archive.GetEntry(entryName);
        entry.Should().NotBeNull("the export ZIP should contain {0}", entryName);
        using var reader = new StreamReader(entry!.Open());
        return await reader.ReadToEndAsync();
    }

    protected static async Task<IReadOnlyList<string>> ReadZipEntryNamesAsync(HttpResponseMessage response)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        return archive.Entries.Select(e => e.Name).ToList();
    }
}