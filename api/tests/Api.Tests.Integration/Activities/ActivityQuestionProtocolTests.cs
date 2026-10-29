using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
using Domain.Activities;
using Domain.Organisations;
using Domain.ValueObjects;
using FluentAssertions;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Activities;

// The 13 August Question Protocol's blue-highlighted changes to the activity-record form schema (BLUM-7620):
// reworded questions, three extended option lists, and the first character limit the schema has ever carried.
// These assertions are the only guard on the seed — SeedFormDesigner writes once into an empty schema, so a
// reverted string would sit in every deployed database with nothing to catch it.
//
// Deliberately NOT an ActivityTestBase: that base wipes the seeded form schema in its setup, and this class is
// entirely about what the seed contains. That wipe is shared-state mutation — the schema is reference data seeded
// once at boot — so any class reading it can find it empty if an ActivityTestBase class is running alongside
// (CI runs xUnit.MaxParallelThreads=2 against one database). Rather than serialise half the suite behind a
// collection, InitializeAsync below re-seeds when it finds the schema gone, which makes this class independent of
// run order. The same race is pre-existing for ActivityDetailsRequiredTests, ActivityPeopleAndIssuesTests, the
// FormDesigner suite and every QuarterlyReportTestBase class; fixing it at the root (ActivityTestBase filling in
// required answers instead of deleting the schema, as QuarterlyReportTestBase already does) is its own ticket.
public class ActivityQuestionProtocolTests(IntegrationTestWebFactory factory)
    : BaseIntegrationTest(factory), IAsyncLifetime
{
    private static readonly Organisation MainOrg = OrganisationsData.MainOrganisation;
    private static readonly Guid MainOrgLeadUserId = Guid.Parse("d0000000-0000-0000-0000-000000000001");

    private static readonly List<ExpectedResultInputDto> CustomResults =
        [new ExpectedResultInputDto(null, "Key result one"), new ExpectedResultInputDto(null, "Key result two")];

    private sealed record AnswerErrorRow(string QuestionCode, string Message);

    private sealed record AnswersInvalidEnvelope(string Error, string Message, List<AnswerErrorRow> AnswerErrors);

    private void AuthenticateAsMainOrgLead() => AuthenticateAs(Role.Lead, MainOrgLeadUserId, MainOrg.Id.Value);

    private async Task<(Guid CategoryId, Guid TaskId)> GetCategoryTypeAsync(string categoryName)
    {
        var response = await Client.GetAsync("activities/taxonomy");
        response.EnsureSuccessStatusCode();
        var taxonomy = await response.ToResponseModel<ActivityTaxonomyDto>();
        var category = taxonomy.Categories.First(c => c.Name == categoryName);
        return (category.Id, taxonomy.Tasks.First(t => t.CategoryId == category.Id).Id);
    }

    private async Task<FormSchemaDto> GetSchemaAsync(Guid categoryId)
    {
        var response = await Client.GetAsync($"activities/form-schema?categoryId={categoryId}");
        response.EnsureSuccessStatusCode();
        return await response.ToResponseModel<FormSchemaDto>();
    }

    private Task<HttpResponseMessage> PostNeedsAssessmentAsync(Guid categoryId, Guid taskId, string plan) =>
        Client.PostAsJsonAsync(
            "activities",
            new CreateActivityRequestDto(
                "Needs assessment activity",
                categoryId,
                taskId,
                null,
                "planned",
                null,
                null,
                false,
                CustomResults,
                [
                    new ActivityAnswerInputDto("NA_WHO_WITH", "police"),
                    new ActivityAnswerInputDto("NA_PLAN", plan),
                ]));

    // Protocol 1.BCDEF.1.4, "Was this activity shared with other local authorities, or will it be shared?", is no
    // longer asked by any category: BLUM-7759 dropped it for Needs assessment, BLUM-8340 for the other four. Its
    // absence is pinned here, next to the wording of the two questions that remain.
    [Theory]
    [InlineData("Needs assessment", "NA")]
    [InlineData("Strategy and leadership", "ST")]
    [InlineData("Licensing, planning and policy", "LIC")]
    [InlineData("Education, campaigns and outreach", "EDU")]
    [InlineData("Help, advice and access to services", "HELP")]
    public async Task ShouldAskTheActivityDetailsQuestions_InTheProtocolsWording(string categoryName, string prefix)
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, _) = await GetCategoryTypeAsync(categoryName);
        var schema = await GetSchemaAsync(categoryId);

        var whoWith = schema.Questions.Should().ContainSingle(q => q.Code == $"{prefix}_WHO_WITH").Subject;
        whoWith.Label.Should().Be("Who have you worked with, or who will you work with?");
        whoWith.Hint.Should().Be("Select all who have helped or will help plan and deliver this activity");

        schema.Questions.Should().ContainSingle(q => q.Code == $"{prefix}_WHO_WITH_OTHER")
            .Which.Label.Should().Be("Who else have you worked with or will you work with?");

        schema.Questions.Should().NotContain(q => q.Code == $"{prefix}_SHARED");
        schema.Questions.Should().NotContain(q => q.Label == "Was this activity shared with other local authorities, or will it be shared?");
    }

    // The whole list, pinned pair by pair rather than by a ContainInOrder subset. WhoWithOptions is the single
    // definition of these choices — SeedActivityDetailsSection writes it to form_question_options for all five
    // categories and the same array backs the five quarterly-report questions that share it — so a drop, a
    // re-merge or a reorder here silently narrows both journeys, and SeedFormDesigner writes once into an empty
    // schema, so a reverted string would sit in every deployed database with nothing else to catch it.
    //
    // Values are asserted alongside labels because the value is what activity_answers and
    // submission_activity_answers store: a renamed value orphans saved answers and makes re-saving an untouched
    // activity fail its option check, where a renamed label is only cosmetic.
    //
    // The last five before "Other" arrived with the 13 August sheet (BLUM-7620); "Affected others" and the three
    // separate health options are BLUM-7697, which found the data map's Primary care / Integrated care board (ICB)
    // / Other health practitioners seeded as one merged checkbox.
    [Fact]
    public async Task ShouldOfferTheDataMapsWhoWithOptions_InOrder()
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, _) = await GetCategoryTypeAsync("Needs assessment");
        var schema = await GetSchemaAsync(categoryId);

        var options = schema.Questions.Single(q => q.Code == "NA_WHO_WITH").Options
            .OrderBy(o => o.DisplayOrder)
            .Select(o => (o.Value, o.Label))
            .ToList();

        options.Should().Equal(
            ("lived_experience", "People with lived experience"),
            ("affected_others", "Affected others"),
            ("licensing", "Licensing"),
            ("planning", "Planning"),
            ("housing", "Housing"),
            ("children", "Children and youth services"),
            ("alcohol", "Alcohol services"),
            ("drug", "Drug treatment services"),
            ("mental_health", "Mental health services"),
            ("police", "Police"),
            ("vcse", "Voluntary, community and social enterprises (VCSEs)"),
            ("welfare", "Welfare services"),
            ("debt", "Debt advice"),
            ("primary_care", "Primary care"),
            ("icb", "Integrated care board (ICB)"),
            ("other_health_practitioners", "Other health practitioners"),
            ("criminal_justice", "Criminal justice system staff"),
            ("dhsc_regional_lead", "DHSC regional lead for gambling"),
            ("la_public_health", "Local authority public health representative"),
            ("local_politicians", "Local politicians"),
            ("coroners", "Coroners"),
            ("teachers_schools", "Teachers or schools"),
            ("other", "Other"));
    }

    // Every question that shares the list, not just the prefilled ones. The quarterly report reads these options
    // from the jsonb column on key_result_questions rather than from form_question_options — a second seeder
    // behind a second guard, so a reseed migration that clears only the form schema leaves this half on the old
    // options, and the prefill then hands the report values its own list no longer offers.
    //
    // The four call sites that pass WhoWith in KeyResultProtocolSeedData. TRN_SERVICE_AREAS is the reason this is
    // not filtered on "_CONTRIBUTORS" alone: it is the one WhoWith question with no prefill, so the unit test's
    // EveryPrefillSource_IsRecognisedAndFitsItsQuestion invariant — which only inspects questions with a non-null
    // PrefillSource — does not reach it, and nothing else pins its options at all.
    [Fact]
    public async Task ShouldOfferTheSameWhoWithOptions_OnEveryQuarterlyReportQuestionThatSharesTheList()
    {
        string[] standaloneCodes = ["LSG_MEMBERS", "TRN_SERVICE_AREAS", "SCR_SERVICES"];

        var seeded = await Context.KeyResultQuestions.AsNoTracking()
            .Where(q => q.Code.EndsWith("_CONTRIBUTORS") || standaloneCodes.Contains(q.Code))
            .ToListAsync();

        // Assert the set before its contents: a code renamed in the seed would otherwise shrink this to the rows
        // that still match and the loop would pass over what is left.
        seeded.Should().Contain(
            q => q.Code.EndsWith("_CONTRIBUTORS"),
            "the contributor questions are seeded for every protocol activity type");
        seeded.Select(q => q.Code).Should().Contain(standaloneCodes);

        foreach (var question in seeded)
        {
            question.Options.Should().NotBeNull("\"{0}\" is a list question", question.Code);
            question.Options!.Select(o => (o.Value, o.Label)).Should().Equal(
                KeyResultProtocolSeedData.WhoWithOptions.Select(o => (o.Value, o.Label)),
                "\"{0}\" offers the shared \"Who have you worked with?\" list",
                question.Code);
        }
    }

    [Theory]
    [InlineData("Education, campaigns and outreach", "EDU")]
    [InlineData("Help, advice and access to services", "HELP")]
    public async Task ShouldAskThePeopleAndIssuesQuestions_InTheProtocolsWording(string categoryName, string prefix)
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, _) = await GetCategoryTypeAsync(categoryName);
        var schema = await GetSchemaAsync(categoryId);

        var whoFor = schema.Questions.Should().ContainSingle(q => q.Code == $"{prefix}_WHO_FOR").Subject;
        whoFor.Label.Should().Be("Who was the activity for, or who will it be for?");
        whoFor.Options.OrderBy(o => o.DisplayOrder).Select(o => o.Label)
            .Should().ContainInOrder("Women and girls", "Men and boys", "Ethnic minority groups");

        schema.Questions.Should().ContainSingle(q => q.Code == $"{prefix}_WHO_FOR_OTHER")
            .Which.Label.Should().Be("Who else was or will the activity be for?");

        var relatedIssues = schema.Questions.Should().ContainSingle(q => q.Code == $"{prefix}_RELATED_ISSUES").Subject;
        relatedIssues.Label.Should().Be("What related issues did it address, or will it address? (optional)");
        relatedIssues.Options.OrderBy(o => o.DisplayOrder).First().Label
            .Should().Be("Experience of gambling harms", "the 13 August protocol leads the list with it");

        var relatedIssuesOther = schema.Questions.Should().ContainSingle(q => q.Code == $"{prefix}_RELATED_ISSUES_OTHER").Subject;
        relatedIssuesOther.Label.Should().Be("What is the other related issue?");
        relatedIssuesOther.ErrorMessage.Should().Be("Enter the related issue");
    }

    [Fact]
    public async Task ShouldAskTheApproachQuestion_WithItsErrorAndCharacterLimit()
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, _) = await GetCategoryTypeAsync("Needs assessment");
        var schema = await GetSchemaAsync(categoryId);

        var plan = schema.Questions.Should().ContainSingle(q => q.Code == "NA_PLAN").Subject;
        plan.Label.Should().Be("Can you tell us about your approach to this activity?");
        plan.Hint.Should().Be("For example the steps you have taken or plan to take");
        plan.ErrorMessage.Should().Be("Enter a description of what you plan to do");
        plan.MaxLength.Should().Be(500);
        plan.MaxLengthErrorMessage.Should().Be("Description of plans must be 500 characters or less");
    }

    [Fact]
    public async Task ShouldRejectAnApproachOverTheCharacterLimit_WithTheSeededMessage()
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, taskId) = await GetCategoryTypeAsync("Needs assessment");

        var response = await PostNeedsAssessmentAsync(categoryId, taskId, new string('a', 501));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var envelope = await response.ToResponseModel<AnswersInvalidEnvelope>();
        envelope.Error.Should().Be("ACTIVITY_ANSWERS_INVALID");
        envelope.AnswerErrors.Should().ContainSingle(e => e.QuestionCode == "NA_PLAN")
            .Which.Message.Should().Be("Description of plans must be 500 characters or less");
        (await Context.Activities.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    // The limit is a ceiling, not a requirement: exactly 500 characters must still save.
    [Fact]
    public async Task ShouldAcceptAnApproachExactlyAtTheCharacterLimit()
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, taskId) = await GetCategoryTypeAsync("Needs assessment");

        var response = await PostNeedsAssessmentAsync(categoryId, taskId, new string('a', 500));

        response.IsSuccessStatusCode.Should().BeTrue(
            "500 characters is inside the limit: {0}", await response.Content.ReadAsStringAsync());
        var dto = await response.ToResponseModel<ActivityDto>();
        (await Context.ActivityAnswers.AsNoTracking().Where(a => a.ActivityId == new ActivityId(dto.Id)).ToListAsync())
            .Should().ContainSingle(a => a.QuestionCode == "NA_PLAN" && a.Value.Length == 500);
    }

    [Fact]
    public async Task ShouldAskTheRoleFundingQuestion_InTheProtocolsWording()
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, _) = await GetCategoryTypeAsync("People and roles");
        var schema = await GetSchemaAsync(categoryId);

        schema.Questions.Should().ContainSingle(q => q.Code == "PR_FUNDING_TIME")
            .Which.Label.Should().Be("How much of the staff member's time is the funding paying for, or will it pay for?");
    }

    public async Task InitializeAsync()
    {
        AuthenticateAsAdmin();
        if (!await Context.Organisations.AsNoTracking().AnyAsync(x => x.Id == MainOrg.Id))
        {
            await Context.Organisations.AddAsync(MainOrg);
            await SaveChangesAsync();
        }

        // Every seeder is guarded on its own tables being empty, so this rebuilds exactly what an
        // ActivityTestBase class deleted and no-ops when the schema is intact.
        if (!await Context.FormSections.AsNoTracking().AnyAsync())
        {
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>().SeedDataAsync();
        }

        await ClearActivitiesAsync();
    }

    public async Task DisposeAsync()
    {
        await ClearActivitiesAsync();
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        await SaveChangesAsync();
        AuthenticateAsAdmin();
    }

    private async Task ClearActivitiesAsync()
    {
        Context.ActivityExpectedResults.RemoveRange(Context.ActivityExpectedResults);
        Context.ActivityAnswers.RemoveRange(Context.ActivityAnswers);
        Context.Activities.RemoveRange(Context.Activities);
        await SaveChangesAsync();
    }
}