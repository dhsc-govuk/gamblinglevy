using System.Net.Http.Json;
using Api.Dtos;
using Domain.Organisations;
using Domain.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.QuarterlyReports;

// Shared setup for the LA-facing quarterly-report surface (BLUM-6611). Collections are created through the admin
// API (so derived statuses and LA assignment behave exactly as production) and reportable activities through the
// activities API. Deliberately does NOT inherit ActivityTestBase: that base clears the seeded form schema, while
// these tests depend on the startup seed staying intact — the key-result protocol, the published
// activity_key_results module (delivery questions) and the category-scoped activity form schema (WHO_WITH prefill).
public abstract class QuarterlyReportTestBase(IntegrationTestWebFactory factory)
    : BaseIntegrationTest(factory), IAsyncLifetime
{
    protected const string KeyResultsModuleCode = "activity_key_results";
    protected const string FundingModuleCode = "funding_spent";
    protected const string FeedbackModuleCode = "strategic_feedback";

    protected const string RnaTaskName = "Contributing to a regional needs assessment";
    protected const string SteeringGroupTaskName = "Setting up and running a local steering group";
    protected const string PeopleTaskName = "Public Health Officer";

    protected static readonly Organisation MainOrg = OrganisationsData.MainOrganisation;
    protected static readonly Organisation SecondOrg = OrganisationsData.SecondOrganisation;

    // A non-LA (VCSE) organisation, used to prove "submitted by all LAs" collections are gated to Local
    // Authorities and never reach VCSE-type orgs.
    protected static readonly Organisation VcseOrg = Organisation.New(
        OrganisationId.New(), "VCSE Test Org", "E06000099", Region.London, OrgType.Vcse, "E1000001", OnboardingStatus.Active);

    protected static readonly Guid MainOrgLeadUserId = Guid.Parse("b0000000-0000-0000-0000-000000000001");
    protected static readonly Guid SecondOrgLeadUserId = Guid.Parse("b0000000-0000-0000-0000-000000000004");
    protected static readonly Guid VcseOrgLeadUserId = Guid.Parse("b0000000-0000-0000-0000-000000000005");

    // Two custom key results satisfy the "Select at least 2" completion rule without depending on the seeded
    // standard definitions.
    protected static readonly IReadOnlyList<ExpectedResultInputDto> DefaultExpectedResults =
    [
        new(null, "Key result one"),
        new(null, "Key result two"),
    ];

    private IReadOnlyList<Guid> _formModuleIds = [];

    protected static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    protected void AuthenticateAsMainOrgLead() => AuthenticateAs(Role.Lead, MainOrgLeadUserId, MainOrg.Id.Value);

    protected void AuthenticateAsSecondOrgLead() => AuthenticateAs(Role.Lead, SecondOrgLeadUserId, SecondOrg.Id.Value);

    protected void AuthenticateAsVcseOrgLead() => AuthenticateAs(Role.Lead, VcseOrgLeadUserId, VcseOrg.Id.Value);

    public async Task InitializeAsync()
    {
        AuthenticateAsAdmin();

        // The authenticated admin (AdminsData.MainAdmin) must exist as a real users row because the collection
        // handlers persist it as the audit actor / created-by.
        if (!await Context.Users.AsNoTracking().AnyAsync(u => u.Id == AdminsData.MainAdmin.Id))
        {
            await Context.Users.AddAsync(AdminsData.MainAdmin);
            await SaveChangesAsync();
        }

        await EnsureOrgAsync(MainOrg);
        await EnsureOrgAsync(SecondOrg);
        await EnsureOrgAsync(VcseOrg);
        await ClearOwnedRowsAsync();

        _formModuleIds = (await Context.FormModules.AsNoTracking().ToListAsync())
            .OrderBy(m => m.DisplayOrder)
            .Select(m => m.Id.Value)
            .ToList();
    }

    public async Task DisposeAsync()
    {
        await ClearOwnedRowsAsync();
        AuthenticateAsAdmin();
    }

    // Removes only the rows these tests create. The startup seed (form modules + delivery questions, key-result
    // protocol, activity taxonomy, category form schema) must survive between tests.
    private async Task ClearOwnedRowsAsync()
    {
        Context.SubmissionActivityAnswers.RemoveRange(Context.SubmissionActivityAnswers);
        Context.SubmissionFeedbackAnswers.RemoveRange(Context.SubmissionFeedbackAnswers);
        Context.SubmissionActivityReports.RemoveRange(Context.SubmissionActivityReports);
        Context.SubmissionSpendEntries.RemoveRange(Context.SubmissionSpendEntries);
        Context.SubmissionSections.RemoveRange(Context.SubmissionSections);
        Context.Submissions.RemoveRange(Context.Submissions);
        Context.OrgFundingAllocations.RemoveRange(Context.OrgFundingAllocations);
        Context.ActivityExpectedResults.RemoveRange(Context.ActivityExpectedResults);
        Context.ActivityAnswers.RemoveRange(Context.ActivityAnswers);
        Context.Activities.RemoveRange(Context.Activities);
        Context.Collections.RemoveRange(Context.Collections);
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        await SaveChangesAsync();
    }

    private async Task EnsureOrgAsync(Organisation organisation)
    {
        var exists = await Context.Organisations.AsNoTracking().AnyAsync(x => x.Id == organisation.Id);
        if (!exists)
        {
            await Context.Organisations.AddAsync(organisation);
            await SaveChangesAsync();
        }
    }

    private sealed record CollectionResult(Guid Id, string Name, string Status, bool IsDraft);

    // Publishes a quarterly collection through the admin API, assigned to the given Local Authorities
    // (MainOrg by default). When isSubmittedByAllLas is true the collection is published with the "all LAs"
    // flag and NO assignment rows (mirroring the wizard payload), so it reaches every LA via the flag rather
    // than the join table. The internal admin identity is AsyncLocal-scoped to this helper, so the caller's
    // ambient identity is untouched.
    protected async Task<Guid> CreateQuarterlyCollectionAsync(
        string name,
        DateOnly openDate,
        DateOnly deadline,
        IReadOnlyList<Guid>? localAuthorityIds = null,
        bool isSubmittedByAllLas = false,
        int quarter = 1,
        string financialYear = "2026/27")
    {
        AuthenticateAsAdmin();

        var request = new CreateCollectionRequestDto(
            Name: name,
            Purpose: "Quarterly report tests",
            Type: "quarterly_report",
            FinancialYear: financialYear,
            Quarter: quarter,
            WindowSameAsReportingPeriod: false,
            OpenDate: openDate,
            Deadline: deadline,
            IsSubmittedByAllLas: isSubmittedByAllLas,
            SaveAsDraft: false,
            LocalAuthorityIds: isSubmittedByAllLas ? [] : localAuthorityIds ?? [MainOrg.Id.Value],
            FormModuleIds: _formModuleIds,
            Reminders: null);

        var response = await Client.PostAsJsonAsync("data-collections", request);
        response.IsSuccessStatusCode.Should().BeTrue(
            "creating collection \"{0}\" failed: {1}", name, await response.Content.ReadAsStringAsync());
        var dto = await response.ToResponseModel<CollectionResult>();
        return dto.Id;
    }

    protected Task<Guid> CreateOpenCollectionAsync(
        string name = "2026 Q1 quarterly report",
        IReadOnlyList<Guid>? localAuthorityIds = null) =>
        CreateQuarterlyCollectionAsync(name, Today.AddDays(-1), Today.AddDays(60), localAuthorityIds);

    // An open quarterly collection published with "submitted by all LAs" = Yes and no explicit assignment rows.
    protected Task<Guid> CreateOpenAllLasCollectionAsync(string name = "2026 Q1 all-LAs quarterly report") =>
        CreateQuarterlyCollectionAsync(name, Today.AddDays(-1), Today.AddDays(60), isSubmittedByAllLas: true);

    // An open quarterly collection for a specific quarter / financial year (the "Record funding spent" tests need
    // distinct quarters in the same year to exercise the "spend to date" accumulation).
    protected Task<Guid> CreateOpenQuarterCollectionAsync(int quarter, string name, string financialYear = "2026/27") =>
        CreateQuarterlyCollectionAsync(name, Today.AddDays(-1), Today.AddDays(60), quarter: quarter, financialYear: financialYear);

    // A published maturity-assessment collection (no quarter — it covers the whole financial year). Maturity
    // assessments have no submission flow, so they must be excluded from the DHSC submission-visibility surfaces.
    protected async Task<Guid> CreateMaturityAssessmentCollectionAsync(
        string name = "2026/27 maturity assessment",
        DateOnly? openDate = null,
        DateOnly? deadline = null,
        IReadOnlyList<Guid>? localAuthorityIds = null)
    {
        AuthenticateAsAdmin();

        var request = new CreateCollectionRequestDto(
            Name: name,
            Purpose: "Maturity assessment tests",
            Type: "maturity_assessment",
            FinancialYear: "2026/27",
            Quarter: null,
            WindowSameAsReportingPeriod: false,
            OpenDate: openDate ?? Today.AddDays(-1),
            Deadline: deadline ?? Today.AddDays(60),
            IsSubmittedByAllLas: false,
            SaveAsDraft: false,
            LocalAuthorityIds: localAuthorityIds ?? [MainOrg.Id.Value],
            FormModuleIds: _formModuleIds,
            Reminders: null);

        var response = await Client.PostAsJsonAsync("data-collections", request);
        response.IsSuccessStatusCode.Should().BeTrue(
            "creating maturity assessment \"{0}\" failed: {1}", name, await response.Content.ReadAsStringAsync());
        var dto = await response.ToResponseModel<CollectionResult>();
        return dto.Id;
    }

    protected async Task CloseCollectionAsync(Guid collectionId)
    {
        AuthenticateAsAdmin();
        var response = await Client.PostAsync($"data-collections/{collectionId}/close", null);
        response.IsSuccessStatusCode.Should().BeTrue();
    }

    protected async Task<ActivityTaxonomyDto> GetTaxonomyAsync()
    {
        var response = await Client.GetAsync("activities/taxonomy");
        response.EnsureSuccessStatusCode();
        return await response.ToResponseModel<ActivityTaxonomyDto>();
    }

    // The seeded (category, type) pair for an activity-type name.
    protected async Task<(Guid CategoryId, Guid TaskId)> GetTypeAsync(string taskName)
    {
        var taxonomy = await GetTaxonomyAsync();
        var task = taxonomy.Tasks.First(t => t.Name == taskName);
        return (task.CategoryId, task.Id);
    }

    // Standard key-result selections for a type, validated against the seeded taxonomy.
    protected async Task<IReadOnlyList<ExpectedResultInputDto>> StandardResultsAsync(Guid taskId, params string[] codes)
    {
        var taxonomy = await GetTaxonomyAsync();
        var available = taxonomy.KeyResults.Where(k => k.TaskId == taskId).Select(k => k.Code).ToHashSet();
        codes.Should().OnlyContain(c => available.Contains(c), "every requested standard key result must be seeded for the type");
        return codes.Select(c => new ExpectedResultInputDto(c, null)).ToList();
    }

    // Creates a completed (reportable when status is active/closed/finished) activity as the current identity.
    protected async Task<ActivityDto> CreateCompletedActivityAsync(
        string taskName = RnaTaskName,
        string status = "active",
        IReadOnlyList<ExpectedResultInputDto>? expectedResults = null,
        IReadOnlyList<ActivityAnswerInputDto>? answers = null,
        string? title = null)
    {
        var (categoryId, taskId) = await GetTypeAsync(taskName);
        var isActive = status == "active";
        var request = new CreateActivityRequestDto(
            title ?? $"{taskName} activity",
            categoryId,
            taskId,
            null,
            status,
            isActive ? 4 : null,
            isActive ? 2026 : null,
            false,
            expectedResults ?? DefaultExpectedResults,
            await WithRequiredDetailAnswersAsync(categoryId, taskId, answers));

        var response = await Client.PostAsJsonAsync("activities", request);
        response.IsSuccessStatusCode.Should().BeTrue(
            "creating activity failed: {0}", await response.Content.ReadAsStringAsync());
        return await response.ToResponseModel<ActivityDto>();
    }

    // The category's seeded "Activity details" questions are mandatory (BLUM-7041), so an activity cannot be
    // completed until they are answered. These tests report on activities rather than exercising those questions,
    // so fill in whatever the caller left out — anything the caller did answer wins, keeping the schema (and the
    // activity answers that prefill the report's contributor questions) intact.
    protected async Task<IReadOnlyList<ActivityAnswerInputDto>> WithRequiredDetailAnswersAsync(
        Guid categoryId, Guid taskId, IReadOnlyList<ActivityAnswerInputDto>? answers)
    {
        var response = await Client.GetAsync($"activities/form-schema?categoryId={categoryId}");
        response.EnsureSuccessStatusCode();
        var schema = await response.ToResponseModel<FormSchemaDto>();

        var result = (answers ?? []).ToList();

        // Blank is not answered — the server prunes empty values before enforcing required, so mirror that or a
        // caller-supplied blank would suppress the fill and 422 instead.
        var map = result
            .Where(a => !string.IsNullOrWhiteSpace(a.Value))
            .ToDictionary(a => a.QuestionCode, a => a.Value);

        // Sections with no ActivityTaskId apply to the whole category; the rest only to the selected type
        // (FormSection.AppliesToTask). The endpoint already returns active sections/questions only.
        var applicable = schema.Sections
            .Where(s => s.ActivityTaskId == null || s.ActivityTaskId == taskId)
            .Select(s => s.Id)
            .ToHashSet();

        // Display order walks parents before their conditional children, so by the time a child is considered the
        // parent's answer — the caller's or the default below — is already in the map and decides visibility.
        var required = schema.Questions
            .Where(q => q.IsRequired && applicable.Contains(q.SectionId))
            .OrderBy(q => q.DisplayOrder);

        foreach (var question in required)
        {
            if (map.ContainsKey(question.Code) || !IsVisible(question, map))
            {
                continue;
            }

            var value = question.Options.Count > 0
                ? question.Options.OrderBy(o => o.DisplayOrder).First().Value
                : $"{question.Code} test answer";
            result.Add(new ActivityAnswerInputDto(question.Code, value));
            map[question.Code] = value;
        }

        return result;
    }

    // Mirrors ActivityAnswerValidator.IsVisible — a question is visible unless a conditional rule's parent answer
    // does not match. Kept in step with it deliberately: a narrower rule here silently yields a 422 for a question
    // this helper believed was hidden.
    private static bool IsVisible(FormQuestionDto question, IReadOnlyDictionary<string, string?> answers)
    {
        if (string.IsNullOrWhiteSpace(question.ConditionalQuestionCode) || string.IsNullOrWhiteSpace(question.ConditionalValue))
        {
            return true;
        }

        if (!answers.TryGetValue(question.ConditionalQuestionCode, out var parent) || string.IsNullOrWhiteSpace(parent))
        {
            return false;
        }

        var allowed = Split(question.ConditionalValue);
        return Split(parent).Any(allowed.Contains);
    }

    private static string[] Split(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // An RNA activity with two standard key results — the protocol questions resolve from these.
    protected async Task<ActivityDto> CreateRnaActivityAsync(string status = "active", string? title = null)
    {
        var (_, taskId) = await GetTypeAsync(RnaTaskName);
        var results = await StandardResultsAsync(taskId, "rna_contributors", "rna_published");
        return await CreateCompletedActivityAsync(RnaTaskName, status, results, title: title);
    }

    protected async Task<SubmissionTaskListDto> CreateSubmissionAsync(Guid collectionId)
    {
        var response = await Client.PostAsync($"quarterly-report/{collectionId}/submission", null);
        response.IsSuccessStatusCode.Should().BeTrue(
            "creating the submission failed: {0}", await response.Content.ReadAsStringAsync());
        return await response.ToResponseModel<SubmissionTaskListDto>();
    }

    protected async Task<SubmissionTaskListDto> ConfirmActivitiesAsync(Guid collectionId)
    {
        var response = await Client.PostAsync($"quarterly-report/{collectionId}/confirm-activities", null);
        response.IsSuccessStatusCode.Should().BeTrue(
            "confirming activities failed: {0}", await response.Content.ReadAsStringAsync());
        return await response.ToResponseModel<SubmissionTaskListDto>();
    }

    protected async Task<SubmissionTaskListDto> GetTaskListAsync(Guid collectionId)
    {
        var response = await Client.GetAsync($"quarterly-report/{collectionId}/task-list");
        response.EnsureSuccessStatusCode();
        return await response.ToResponseModel<SubmissionTaskListDto>();
    }

    protected async Task<ActivityReportQuestionsDto> GetActivityQuestionsAsync(Guid collectionId, Guid activityId)
    {
        var response = await Client.GetAsync($"quarterly-report/{collectionId}/activities/{activityId}/questions");
        response.EnsureSuccessStatusCode();
        return await response.ToResponseModel<ActivityReportQuestionsDto>();
    }

    protected Task<HttpResponseMessage> SaveAnswersAsync(
        Guid collectionId,
        Guid activityId,
        bool saveAsDraft,
        params (string Code, string? Value)[] answers)
    {
        var request = new SaveActivityReportRequestDto(
            saveAsDraft,
            answers.Select(a => new ActivityAnswerInputDto(a.Code, a.Value)).ToList());
        return Client.PutAsJsonAsync($"quarterly-report/{collectionId}/activities/{activityId}/answers", request);
    }

    // The check-your-answers Continue: no body — the saved draft answers are validated server-side.
    protected Task<HttpResponseMessage> CompleteActivityAsync(Guid collectionId, Guid activityId) =>
        Client.PostAsync($"quarterly-report/{collectionId}/activities/{activityId}/complete", null);

    // Completes an activity's report the way the frontend does: draft-save an answer for every required
    // unconditional question, then complete via the check-your-answers endpoint (a save alone never completes).
    protected async Task CompleteActivityReportAsync(Guid collectionId, Guid activityId)
    {
        var questions = await GetActivityQuestionsAsync(collectionId, activityId);
        var answers = questions.Questions
            .Where(q => q.IsRequired && string.IsNullOrEmpty(q.ConditionalQuestionCode))
            .Select(q => (q.Code, (string?)AnswerValueFor(q)))
            .ToArray();

        var save = await SaveAnswersAsync(collectionId, activityId, saveAsDraft: true, answers);
        save.IsSuccessStatusCode.Should().BeTrue(
            "saving the report answers for activity {0} failed: {1}", activityId, await save.Content.ReadAsStringAsync());

        var complete = await CompleteActivityAsync(collectionId, activityId);
        complete.IsSuccessStatusCode.Should().BeTrue(
            "completing the report for activity {0} failed: {1}", activityId, await complete.Content.ReadAsStringAsync());
    }

    private static string AnswerValueFor(ReportQuestionDto question)
    {
        if (question.Options.Count > 0)
        {
            return question.Options[0].Value;
        }

        return question.QuestionType == "Number" ? "5" : "Progress made this quarter";
    }

    // --- "Record funding spent" helpers ---

    protected async Task<FundingSpendDto> GetFundingSpendAsync(Guid collectionId)
    {
        var response = await Client.GetAsync($"quarterly-report/{collectionId}/funding-spend");
        response.EnsureSuccessStatusCode();
        return await response.ToResponseModel<FundingSpendDto>();
    }

    protected Task<HttpResponseMessage> SaveFundingActualsAsync(
        Guid collectionId,
        bool saveAsDraft,
        decimal? otherAmount,
        params (Guid ActivityId, decimal? Amount)[] activities)
    {
        var request = new SaveFundingActualsRequestDto(
            saveAsDraft,
            otherAmount,
            activities.Select(a => new FundingActivitySpendInputDto(a.ActivityId, a.Amount)).ToList());
        return Client.PutAsJsonAsync($"quarterly-report/{collectionId}/funding-spend/actuals", request);
    }

    protected Task<HttpResponseMessage> SaveFundingForecastAsync(
        Guid collectionId,
        bool saveAsDraft,
        IReadOnlyList<FundingActivityForecastInputDto> activities,
        IReadOnlyList<FundingQuarterAmountInputDto> other)
    {
        var request = new SaveFundingForecastRequestDto(saveAsDraft, activities, other);
        return Client.PutAsJsonAsync($"quarterly-report/{collectionId}/funding-spend/forecast", request);
    }

    protected Task<HttpResponseMessage> CompleteFundingSpendAsync(Guid collectionId) =>
        Client.PostAsync($"quarterly-report/{collectionId}/funding-spend/complete", null);

    // --- "Provide strategic feedback" helpers ---

    protected async Task<StrategicFeedbackDto> GetStrategicFeedbackAsync(Guid collectionId)
    {
        var response = await Client.GetAsync($"quarterly-report/{collectionId}/strategic-feedback");
        response.EnsureSuccessStatusCode();
        return await response.ToResponseModel<StrategicFeedbackDto>();
    }

    protected Task<HttpResponseMessage> SaveStrategicFeedbackAsync(
        Guid collectionId,
        bool saveAsDraft,
        params (string Code, string? Value)[] answers)
    {
        var request = new SaveStrategicFeedbackRequestDto(
            saveAsDraft,
            answers.Select(a => new StrategicFeedbackAnswerInputDto(a.Code, a.Value)).ToList());
        return Client.PutAsJsonAsync($"quarterly-report/{collectionId}/strategic-feedback", request);
    }

    protected Task<HttpResponseMessage> CompleteStrategicFeedbackAsync(Guid collectionId) =>
        Client.PostAsync($"quarterly-report/{collectionId}/strategic-feedback/complete", null);

    // Answers every feedback question with a plausible value and completes the section. The caller must already be
    // authenticated as a Main-org activity manager.
    protected async Task CompleteStrategicFeedbackSectionAsync(Guid collectionId)
    {
        var feedback = await GetStrategicFeedbackAsync(collectionId);
        var answers = feedback.Pages
            .SelectMany(p => p.Questions)
            .Select(q => (q.Code, (string?)(q.Options.FirstOrDefault()?.Value ?? "Feedback answer for this quarter.")))
            .ToArray();

        (await SaveStrategicFeedbackAsync(collectionId, saveAsDraft: true, answers)).EnsureSuccessStatusCode();
        (await CompleteStrategicFeedbackAsync(collectionId)).EnsureSuccessStatusCode();
    }

    // Fills every reportable activity + the "other" lines (both screens) with plausible amounts and completes the
    // section. The caller must already be authenticated as a Main-org activity manager.
    protected async Task CompleteFundingSpendSectionAsync(
        Guid collectionId,
        decimal perActivityActual = 100m,
        decimal otherActual = 20m,
        decimal perQuarterForecast = 50m,
        decimal otherForecast = 10m)
    {
        var funding = await GetFundingSpendAsync(collectionId);

        var actuals = funding.Activities.Select(a => (a.ActivityId, (decimal?)perActivityActual)).ToArray();
        (await SaveFundingActualsAsync(collectionId, saveAsDraft: true, otherActual, actuals)).EnsureSuccessStatusCode();

        var forecastActivities = funding.Activities
            .Select(a => new FundingActivityForecastInputDto(
                a.ActivityId,
                funding.RemainingQuarters.Select(q => new FundingQuarterAmountInputDto(q.Quarter, perQuarterForecast)).ToList()))
            .ToList();
        var otherForecastList = funding.RemainingQuarters
            .Select(q => new FundingQuarterAmountInputDto(q.Quarter, otherForecast))
            .ToList();
        (await SaveFundingForecastAsync(collectionId, saveAsDraft: true, forecastActivities, otherForecastList)).EnsureSuccessStatusCode();

        (await CompleteFundingSpendAsync(collectionId)).EnsureSuccessStatusCode();
    }
}