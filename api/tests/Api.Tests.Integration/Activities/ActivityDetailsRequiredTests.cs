using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
using Domain.Activities;
using Domain.Organisations;
using Domain.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Activities;

// The shared "Activity details" and "Additional information" sections used by every non-People category
// (BLUM-7041, BLUM-7120): every displayed question is mandatory, while the activity's name stays optional. Does not inherit
// ActivityTestBase — that base clears the seeded form schema, and these tests assert against the real seed.
public class ActivityDetailsRequiredTests(IntegrationTestWebFactory factory)
    : BaseIntegrationTest(factory), IAsyncLifetime
{
    private const string CategoryName = "Needs assessment";

    private static readonly Organisation MainOrg = OrganisationsData.MainOrganisation;
    private static readonly Guid MainOrgLeadUserId = Guid.Parse("d0000000-0000-0000-0000-000000000001");

    private sealed record AnswerErrorRow(string QuestionCode, string Message);

    private sealed record AnswersInvalidEnvelope(string Error, string Message, List<AnswerErrorRow> AnswerErrors);

    private void AuthenticateAsMainOrgLead() => AuthenticateAs(Role.Lead, MainOrgLeadUserId, MainOrg.Id.Value);

    // Needs assessment keeps the protocol's minimum of two key results, so two custom entries complete it
    // without depending on the seeded standard definitions.
    private static List<ExpectedResultInputDto> CustomResults() =>
        [new ExpectedResultInputDto(null, "Key result one"), new ExpectedResultInputDto(null, "Key result two")];

    // Every Activity details and Additional information question answered — the completion baseline.
    private static List<ActivityAnswerInputDto> CompleteAnswers() =>
    [
        new ActivityAnswerInputDto("NA_WHO_WITH", "licensing"),
        new ActivityAnswerInputDto("NA_SHARED", "no"),
        new ActivityAnswerInputDto("NA_PLAN", "Run a local needs assessment"),
    ];

    private async Task<(Guid CategoryId, Guid TaskId)> GetCategoryAndTypeAsync()
    {
        var response = await Client.GetAsync("activities/taxonomy");
        response.EnsureSuccessStatusCode();
        var taxonomy = await response.ToResponseModel<ActivityTaxonomyDto>();
        var category = taxonomy.Categories.First(c => c.Name == CategoryName);
        var task = taxonomy.Tasks.First(t => t.CategoryId == category.Id);
        return (category.Id, task.Id);
    }

    private async Task<HttpResponseMessage> PostActivityAsync(
        IReadOnlyList<ActivityAnswerInputDto>? answers, bool saveAsDraft = false, string? title = "Needs assessment activity")
    {
        var (categoryId, taskId) = await GetCategoryAndTypeAsync();
        return await Client.PostAsJsonAsync(
            "activities",
            new CreateActivityRequestDto(
                title,
                categoryId,
                taskId,
                null,
                saveAsDraft ? null : "planned",
                null,
                null,
                saveAsDraft,
                saveAsDraft ? null : CustomResults(),
                answers));
    }

    [Fact]
    public async Task ShouldSeedActivityDetailsAndAdditionalInformationQuestionsAsRequired()
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, _) = await GetCategoryAndTypeAsync();

        var response = await Client.GetAsync($"activities/form-schema?categoryId={categoryId}");
        response.EnsureSuccessStatusCode();
        var schema = await response.ToResponseModel<FormSchemaDto>();

        var detailsSection = schema.Sections.Should().ContainSingle(s => s.Title == "Activity details").Subject;
        detailsSection.DisplayOrder.Should().Be(0);
        var additionalSection = schema.Sections.Should().ContainSingle(s => s.Title == "Additional information").Subject;
        additionalSection.DisplayOrder.Should().Be(1);

        foreach (var code in new[] { "NA_WHO_WITH", "NA_WHO_WITH_OTHER", "NA_SHARED" })
        {
            var question = schema.Questions.Should().ContainSingle(q => q.Code == code).Subject;
            question.SectionId.Should().Be(detailsSection.Id);
            question.IsRequired.Should().BeTrue("{0} is mandatory on the Activity details step (BLUM-7041)", code);
        }

        // Protocol 1.BCDEF.1.4 — asked for every generic category, Needs assessment included (BLUM-7081).
        var shared = schema.Questions.Single(q => q.Code == "NA_SHARED");
        shared.Label.Should().Be("Is this activity shared with other local authorities?");
        shared.QuestionType.Should().Be("Radio");
        shared.Options.OrderBy(o => o.DisplayOrder).Select(o => (o.Value, o.Label))
            .Should().Equal(("yes", "Yes"), ("no", "No, it's a local activity"));

        var plan = schema.Questions.Should().ContainSingle(q => q.Code == "NA_PLAN").Subject;
        plan.SectionId.Should().Be(additionalSection.Id);
        plan.IsRequired.Should().BeTrue("the plan is mandatory on the Additional information step");
        plan.Label.Should().Be("What do you plan to do?");
        plan.Hint.Should().Be("Use this space to add any other information about your approach and the steps you'll take");
    }

    // BLUM-7081 and BLUM-7120 were per-category divergences: Needs assessment silently stopped asking protocol
    // question 1.BCDEF.1.4, then Education and Help never asked it at all. Asserting one category would let the
    // same drift recur on the others, so pin all five non-People categories.
    [Theory]
    [InlineData("Needs assessment", "NA_SHARED")]
    [InlineData("Strategy and leadership", "ST_SHARED")]
    [InlineData("Licensing, planning and policy", "LIC_SHARED")]
    [InlineData("Education, campaigns and outreach", "EDU_SHARED")]
    [InlineData("Help, advice and access to services", "HELP_SHARED")]
    public async Task ShouldAskTheSharedAuthorityQuestion_ForEveryNonPeopleCategory(string categoryName, string code)
    {
        AuthenticateAsMainOrgLead();

        var schema = await GetSchemaAsync(categoryName);

        // The endpoint only returns active questions, so ContainSingle above already proves the question is
        // active — no separate IsActive assertion would ever be exercisable here.
        var section = schema.Sections.Should().ContainSingle(s => s.Title == "Activity details").Subject;
        var question = schema.Questions.Should().ContainSingle(q => q.Code == code).Subject;
        question.SectionId.Should().Be(section.Id);
        question.IsRequired.Should().BeTrue();
        question.QuestionType.Should().Be("Radio");
        question.Label.Should().Be("Is this activity shared with other local authorities?");
        question.Options.OrderBy(o => o.DisplayOrder).Select(o => (o.Value, o.Label))
            .Should().Equal(("yes", "Yes"), ("no", "No, it's a local activity"));
    }

    // People and roles is the only category outside the shared Activity details protocol block — it has its own
    // Role details screen. (No EndsWith("_SHARED") assert here: PR_ROLE_SHARED legitimately matches it.)
    [Fact]
    public async Task ShouldNotAskTheActivityDetailsQuestions_ForPeopleAndRoles()
    {
        AuthenticateAsMainOrgLead();

        var schema = await GetSchemaAsync("People and roles");

        schema.Sections.Should().NotContain(s => s.Title == "Activity details");
        schema.Questions.Should().NotContain(q => q.Code.EndsWith("_WHO_WITH"));
    }

    private async Task<FormSchemaDto> GetSchemaAsync(string categoryName)
    {
        var taxonomyResponse = await Client.GetAsync("activities/taxonomy");
        taxonomyResponse.EnsureSuccessStatusCode();
        var taxonomy = await taxonomyResponse.ToResponseModel<ActivityTaxonomyDto>();
        var category = taxonomy.Categories.First(c => c.Name == categoryName);

        var response = await Client.GetAsync($"activities/form-schema?categoryId={category.Id}");
        response.EnsureSuccessStatusCode();
        return await response.ToResponseModel<FormSchemaDto>();
    }

    // Every required detail/additional field left blank must block completion.
    [Fact]
    public async Task ShouldRejectCompletion_WhenActivityDetailsAnswersAreMissing()
    {
        AuthenticateAsMainOrgLead();

        var response = await PostActivityAsync(answers: null);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var envelope = await response.ToResponseModel<AnswersInvalidEnvelope>();
        envelope.Error.Should().Be("ACTIVITY_ANSWERS_INVALID");
        envelope.AnswerErrors.Should().Contain(e => e.QuestionCode == "NA_WHO_WITH");
        envelope.AnswerErrors.Should().Contain(e => e.QuestionCode == "NA_SHARED");
        envelope.AnswerErrors.Should().Contain(e => e.QuestionCode == "NA_PLAN");

        // The conditional follow-up is not revealed while NA_WHO_WITH is unanswered, so it must not be enforced.
        envelope.AnswerErrors.Should().NotContain(e => e.QuestionCode == "NA_WHO_WITH_OTHER");
        (await Context.Activities.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ShouldAllowDraft_WithBlankActivityDetails()
    {
        AuthenticateAsMainOrgLead();

        var response = await PostActivityAsync(answers: null, saveAsDraft: true);

        response.IsSuccessStatusCode.Should().BeTrue(
            "a draft skips required enforcement: {0}", await response.Content.ReadAsStringAsync());
        var dto = await response.ToResponseModel<ActivityDto>();
        (await Context.Activities.AsNoTracking().FirstAsync(x => x.Id == new ActivityId(dto.Id))).IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task ShouldRequireWhoWithOther_WhenOtherIsSelected()
    {
        AuthenticateAsMainOrgLead();

        var answers = new List<ActivityAnswerInputDto>
        {
            new("NA_WHO_WITH", "other"),
            new("NA_SHARED", "no"),
            new("NA_PLAN", "Run a local needs assessment"),
        };
        var response = await PostActivityAsync(answers);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var envelope = await response.ToResponseModel<AnswersInvalidEnvelope>();
        envelope.AnswerErrors.Should().Contain(e => e.QuestionCode == "NA_WHO_WITH_OTHER");
        (await Context.Activities.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ShouldComplete_WhenOtherIsNotSelected_AndTheFollowUpIsBlank()
    {
        AuthenticateAsMainOrgLead();

        var response = await PostActivityAsync(CompleteAnswers());

        response.IsSuccessStatusCode.Should().BeTrue(
            "NA_WHO_WITH_OTHER is hidden unless \"other\" is selected: {0}", await response.Content.ReadAsStringAsync());
        var dto = await response.ToResponseModel<ActivityDto>();

        var saved = await Context.Activities.AsNoTracking().FirstAsync(x => x.Id == new ActivityId(dto.Id));
        saved.IsComplete.Should().BeTrue();

        var answers = await Context.ActivityAnswers.AsNoTracking().Where(a => a.ActivityId == saved.Id).ToListAsync();
        answers.Should().ContainSingle(a => a.QuestionCode == "NA_WHO_WITH" && a.Value == "licensing");
        answers.Should().ContainSingle(a => a.QuestionCode == "NA_SHARED" && a.Value == "no");
    }

    // The edit path enforces required answers harder than create: UpdateActivityCommandHandler derives
    // savingAsDraft as "SaveAsDraft && !activity.IsComplete", so a completed activity always enforces them. It
    // also replaces the answers to every applicable question, so an edit that drops them must be rejected rather
    // than silently wiping what is stored.
    [Fact]
    public async Task ShouldRejectEdit_WhenActivityDetailsAnswersAreOmitted()
    {
        AuthenticateAsMainOrgLead();
        var created = await PostActivityAsync(CompleteAnswers());
        created.IsSuccessStatusCode.Should().BeTrue("setup create failed: {0}", await created.Content.ReadAsStringAsync());
        var dto = await created.ToResponseModel<ActivityDto>();
        var (categoryId, taskId) = await GetCategoryAndTypeAsync();

        var response = await Client.PutAsJsonAsync(
            $"activities/{dto.Id}",
            new UpdateActivityRequestDto("Renamed", categoryId, taskId, null, "planned", null, null, false, CustomResults(), null));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var envelope = await response.ToResponseModel<AnswersInvalidEnvelope>();
        envelope.AnswerErrors.Should().Contain(e => e.QuestionCode == "NA_WHO_WITH");

        // The rejected edit must leave the stored answers alone.
        var stored = await Context.ActivityAnswers.AsNoTracking().Where(a => a.ActivityId == new ActivityId(dto.Id)).ToListAsync();
        stored.Should().ContainSingle(a => a.QuestionCode == "NA_WHO_WITH" && a.Value == "licensing");
    }

    [Fact]
    public async Task ShouldAcceptEdit_WhenActivityDetailsAnswersAreResent()
    {
        AuthenticateAsMainOrgLead();
        var created = await PostActivityAsync(CompleteAnswers());
        var dto = await created.ToResponseModel<ActivityDto>();
        var (categoryId, taskId) = await GetCategoryAndTypeAsync();

        // What the edit screen sends: the activity's own saved answers, with one detail answer changed.
        List<ActivityAnswerInputDto> resent =
        [
            new ActivityAnswerInputDto("NA_WHO_WITH", "police"),
            new ActivityAnswerInputDto("NA_SHARED", "yes"),
            new ActivityAnswerInputDto("NA_PLAN", "Run a local needs assessment"),
        ];
        var response = await Client.PutAsJsonAsync(
            $"activities/{dto.Id}",
            new UpdateActivityRequestDto("Renamed", categoryId, taskId, null, "planned", null, null, false, CustomResults(), resent));

        response.IsSuccessStatusCode.Should().BeTrue("resending every answer satisfies the edit: {0}", await response.Content.ReadAsStringAsync());
        var stored = await Context.ActivityAnswers.AsNoTracking().Where(a => a.ActivityId == new ActivityId(dto.Id)).ToListAsync();
        stored.Should().ContainSingle(a => a.QuestionCode == "NA_WHO_WITH" && a.Value == "police");
    }

    // "What's the name of this activity?" is a structural field outside the schema and stays optional.
    [Fact]
    public async Task ShouldCompleteWithoutAName_WhenActivityDetailsAreAnswered()
    {
        AuthenticateAsMainOrgLead();

        var response = await PostActivityAsync(CompleteAnswers(), title: null);

        response.IsSuccessStatusCode.Should().BeTrue(
            "the activity name is optional (BLUM-7041): {0}", await response.Content.ReadAsStringAsync());
        var dto = await response.ToResponseModel<ActivityDto>();
        (await Context.Activities.AsNoTracking().FirstAsync(x => x.Id == new ActivityId(dto.Id))).IsComplete.Should().BeTrue();
    }

    public async Task InitializeAsync()
    {
        AuthenticateAsAdmin();
        var exists = await Context.Organisations.AsNoTracking().AnyAsync(x => x.Id == MainOrg.Id);
        if (!exists)
        {
            await Context.Organisations.AddAsync(MainOrg);
            await SaveChangesAsync();
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