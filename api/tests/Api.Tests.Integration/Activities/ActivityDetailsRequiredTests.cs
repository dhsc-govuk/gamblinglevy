using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
using Domain.Activities;
using Domain.Forms;
using Domain.Organisations;
using Domain.Users;
using Domain.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Activities;

// The shared "Activity details" and "Additional information" sections used by every non-People category
// (BLUM-7041, BLUM-7120): every displayed question is mandatory, and since the 13 August protocol so is the
// activity's name on completion (BLUM-7620) — a draft may still be saved without one. Does not inherit
// ActivityTestBase — that base clears the seeded form schema, and these tests assert against the real seed.
//
// Needs assessment is the class's category. No generic category asks the shared-authority question any more
// (BLUM-7759, BLUM-8340), so its Activity details step is NA_WHO_WITH plus the conditional NA_WHO_WITH_OTHER, and
// NA_SHARED must not appear in the schema, the required-answer errors or the saved rows.
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

    // Every Activity details and Additional information question answered — the completion baseline. No NA_SHARED:
    // Needs assessment stopped asking it at BLUM-7759.
    private static List<ActivityAnswerInputDto> CompleteAnswers() =>
    [
        new ActivityAnswerInputDto("NA_WHO_WITH", "licensing"),
        new ActivityAnswerInputDto("NA_PLAN", "Run a local needs assessment"),
    ];

    private async Task<(Guid CategoryId, Guid TaskId)> GetCategoryAndTypeAsync(string categoryName = CategoryName)
    {
        var response = await Client.GetAsync("activities/taxonomy");
        response.EnsureSuccessStatusCode();
        var taxonomy = await response.ToResponseModel<ActivityTaxonomyDto>();
        var category = taxonomy.Categories.First(c => c.Name == categoryName);
        var task = taxonomy.Tasks.First(t => t.CategoryId == category.Id);
        return (category.Id, task.Id);
    }

    private async Task<HttpResponseMessage> PostActivityAsync(
        IReadOnlyList<ActivityAnswerInputDto>? answers,
        bool saveAsDraft = false,
        string? title = "Needs assessment activity",
        string categoryName = CategoryName)
    {
        var (categoryId, taskId) = await GetCategoryAndTypeAsync(categoryName);
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

        foreach (var code in new[] { "NA_WHO_WITH", "NA_WHO_WITH_OTHER" })
        {
            var question = schema.Questions.Should().ContainSingle(q => q.Code == code).Subject;
            question.SectionId.Should().Be(detailsSection.Id);
            question.IsRequired.Should().BeTrue("{0} is mandatory on the Activity details step (BLUM-7041)", code);
        }

        // Protocol 1.BCDEF.1.4 is no longer asked by any category (BLUM-7759 for Needs assessment, BLUM-8340 for the rest).
        schema.Questions.Should().NotContain(q => q.Code == "NA_SHARED");

        var plan = schema.Questions.Should().ContainSingle(q => q.Code == "NA_PLAN").Subject;
        plan.SectionId.Should().Be(additionalSection.Id);
        plan.IsRequired.Should().BeTrue("the plan is mandatory on the Additional information step");
        plan.Label.Should().Be("Can you tell us about your approach to this activity?");
        plan.Hint.Should().Be("For example the steps you have taken or plan to take");
    }

    // Protocol 1.BCDEF.1.4, "Was this activity shared with other local authorities, or will it be shared?", is gone
    // from every category's Activity details step: BLUM-7759 dropped it for Needs assessment, BLUM-8340 for the
    // other four. Every generic category is pinned because the question's history is per-category drift (BLUM-7081
    // and BLUM-7120 each lost or never had it on some). The whole set is asserted, with orders, rather than a bare
    // NotContain: a reseed that reintroduced the question under another code would slip past that.
    //
    // A fresh database never seeds {ST,LIC,EDU,HELP}_SHARED, so this proves the seeder. Deployed databases keep the
    // four rows deactivated (BLUM_8340_RemoveActivitySharedQuestion), and this endpoint returns only active
    // questions — the migration was checked against a database seeded before the change.
    [Theory]
    [InlineData("Needs assessment", "NA")]
    [InlineData("Strategy and leadership", "ST")]
    [InlineData("Licensing, planning and policy", "LIC")]
    [InlineData("Education, campaigns and outreach", "EDU")]
    [InlineData("Help, advice and access to services", "HELP")]
    public async Task ShouldNotAskTheSharedAuthorityQuestion_ForAnyGenericCategory(string categoryName, string prefix)
    {
        AuthenticateAsMainOrgLead();

        var schema = await GetSchemaAsync(categoryName);

        var section = schema.Sections.Should().ContainSingle(s => s.Title == "Activity details").Subject;
        schema.Questions.Where(q => q.SectionId == section.Id)
            .OrderBy(q => q.DisplayOrder)
            .Select(q => (q.Code, q.DisplayOrder))
            .Should().Equal(($"{prefix}_WHO_WITH", 0), ($"{prefix}_WHO_WITH_OTHER", 1));
        schema.Questions.Should().NotContain(q => q.Code.EndsWith("_SHARED"));
    }

    // BLUM-8340: removing the question must leave no required answer behind on the categories that used to ask it.
    // Education and Help are covered by ActivityPeopleAndIssuesTests, whose complete answer set no longer carries
    // {prefix}_SHARED.
    [Theory]
    [InlineData("Strategy and leadership", "ST")]
    [InlineData("Licensing, planning and policy", "LIC")]
    public async Task ShouldCompleteActivity_WithoutASharedAuthorityAnswer(string categoryName, string prefix)
    {
        AuthenticateAsMainOrgLead();

        var response = await PostActivityAsync(
            [
                new ActivityAnswerInputDto($"{prefix}_WHO_WITH", "licensing"),
                new ActivityAnswerInputDto($"{prefix}_PLAN", "Deliver it locally"),
            ],
            title: "Generic activity",
            categoryName: categoryName);

        response.IsSuccessStatusCode.Should().BeTrue(
            "1.BCDEF.1.4 is no longer asked, so it cannot be demanded: {0}", await response.Content.ReadAsStringAsync());
        var dto = await response.ToResponseModel<ActivityDto>();
        (await Context.Activities.AsNoTracking().FirstAsync(x => x.Id == new ActivityId(dto.Id))).IsComplete.Should().BeTrue();
    }

    // A tab that fetched the schema before the deploy still shows the radio and sends its answer. The save must still
    // succeed: the server checks answers against its own active schema, not the request. The value is dropped rather
    // than stored against a question nobody is asked any more.
    [Fact]
    public async Task ShouldAcceptButNotStoreASharedAuthorityAnswer_FromAStaleForm()
    {
        AuthenticateAsMainOrgLead();

        var response = await PostActivityAsync(
            [
                new ActivityAnswerInputDto("ST_WHO_WITH", "licensing"),
                new ActivityAnswerInputDto("ST_SHARED", "yes"),
                new ActivityAnswerInputDto("ST_PLAN", "Deliver it locally"),
            ],
            title: "Strategy activity",
            categoryName: "Strategy and leadership");

        response.IsSuccessStatusCode.Should().BeTrue(
            "a stale form's extra answer must not block the save: {0}", await response.Content.ReadAsStringAsync());
        var dto = await response.ToResponseModel<ActivityDto>();
        var stored = await Context.ActivityAnswers.AsNoTracking().Where(a => a.ActivityId == new ActivityId(dto.Id)).ToListAsync();
        stored.Should().ContainSingle(a => a.QuestionCode == "ST_WHO_WITH");
        stored.Should().NotContain(a => a.QuestionCode == "ST_SHARED");
    }

    // The deployed state after BLUM_8340_RemoveActivitySharedQuestion, rebuilt here because a fresh test database
    // never seeds the question: ST_SHARED still exists, required and with its options, but deactivated. An activity
    // holds an answer to it. This pins what the migration relies on.
    //   • The inactive question is neither served nor enforced. Completing without it succeeds.
    //   • The saved answer survives both a read and an edit. UpdateActivityCommand replaces only the codes of the
    //     applicable questions and of what the request submitted, and the service is live, so nothing may clear it.
    [Fact]
    public async Task ShouldNeitherAskNorClearTheDeactivatedSharedAuthorityQuestion()
    {
        AuthenticateAsMainOrgLead();
        const string strategy = "Strategy and leadership";
        var detailsSectionId = (await GetSchemaAsync(strategy)).Sections.Single(s => s.Title == "Activity details").Id;

        var actor = new UserId(MainOrgLeadUserId);
        var shared = FormQuestion.Create(
            FormQuestionId.New(),
            new FormSectionId(detailsSectionId),
            "ST_SHARED",
            "Was this activity shared with other local authorities, or will it be shared?",
            QuestionType.Radio,
            3,
            actor,
            isRequired: true,
            utcNow: DateTime.UtcNow);
        shared.AddOption(FormQuestionOptionId.New(), "yes", "Yes", 0, DateTime.UtcNow);
        shared.AddOption(FormQuestionOptionId.New(), "no", "No, it's a local activity", 1, DateTime.UtcNow);
        shared.Deactivate(DateTime.UtcNow);
        await Context.FormQuestions.AddAsync(shared);
        await SaveChangesAsync();

        (await GetSchemaAsync(strategy)).Questions.Should().NotContain(q => q.Code == "ST_SHARED");

        var created = await PostActivityAsync(
            [
                new ActivityAnswerInputDto("ST_WHO_WITH", "licensing"),
                new ActivityAnswerInputDto("ST_PLAN", "Deliver it locally"),
            ],
            title: "Strategy activity",
            categoryName: strategy);
        created.IsSuccessStatusCode.Should().BeTrue(
            "a deactivated required question must not be enforced: {0}", await created.Content.ReadAsStringAsync());
        var dto = await created.ToResponseModel<ActivityDto>();
        var activityId = new ActivityId(dto.Id);

        await Context.ActivityAnswers.AddAsync(ActivityAnswer.Record(ActivityAnswerId.New(), activityId, shared.Id, "ST_SHARED", "yes", actor, DateTime.UtcNow));
        await SaveChangesAsync();

        (await Client.GetAsync($"activities/{dto.Id}")).EnsureSuccessStatusCode();

        var (categoryId, taskId) = await GetCategoryAndTypeAsync(strategy);
        List<ActivityAnswerInputDto> resent =
        [
            new ActivityAnswerInputDto("ST_WHO_WITH", "police"),
            new ActivityAnswerInputDto("ST_PLAN", "Deliver it locally"),
        ];
        var response = await Client.PutAsJsonAsync(
            $"activities/{dto.Id}",
            new UpdateActivityRequestDto("Renamed", categoryId, taskId, null, "planned", null, null, false, CustomResults(), resent));

        response.IsSuccessStatusCode.Should().BeTrue("the edit must not need the withdrawn answer: {0}", await response.Content.ReadAsStringAsync());
        var stored = await Context.ActivityAnswers.AsNoTracking().Where(a => a.ActivityId == activityId).ToListAsync();
        stored.Should().ContainSingle(a => a.QuestionCode == "ST_WHO_WITH" && a.Value == "police");
        stored.Should().ContainSingle(a => a.QuestionCode == "ST_SHARED" && a.Value == "yes");
    }

    // People and roles is the only category outside the shared Activity details protocol block — it has its own
    // Role details screen. The EndsWith("_SHARED") assert is now safe: PR_ROLE_SHARED used to match it, and
    // BLUM-7871 removed that question, so no shared-authority code belongs on this category at all.
    [Fact]
    public async Task ShouldNotAskTheActivityDetailsQuestions_ForPeopleAndRoles()
    {
        AuthenticateAsMainOrgLead();

        var schema = await GetSchemaAsync("People and roles");

        schema.Sections.Should().NotContain(s => s.Title == "Activity details");
        schema.Questions.Should().NotContain(q => q.Code.EndsWith("_WHO_WITH"));
        schema.Questions.Should().NotContain(q => q.Code.EndsWith("_SHARED"));
    }

    // BLUM-7871 withdrew "Is the role shared with other local authorities?" (PR_ROLE_SHARED) from the Role
    // details screen: Joint partnership is its own activity category since BLUM-7860, so a role funded across
    // authorities is recorded as a partnership record instead. Assert the whole set rather than the absence —
    // a reseed that reintroduced the question under another code would slip past a bare NotContain, which is
    // exactly the reason ShouldNotAskTheSharedAuthorityQuestion_ForAnyGenericCategory above is written this way.
    // The orders are pinned as part of that shape, not because a gap would break anything — questions are
    // rendered in sorted order. Pinning them here catches a reseed that reorders the two survivors, which IS
    // user-visible; if a future ticket reorders them on purpose, update the expectation.
    [Fact]
    public async Task ShouldNotAskTheSharedRoleQuestion_ForPeopleAndRoles()
    {
        AuthenticateAsMainOrgLead();

        var schema = await GetSchemaAsync("People and roles");

        var section = schema.Sections.Should().ContainSingle(s => s.Title == "Role details").Subject;
        schema.Questions.Where(q => q.SectionId == section.Id)
            .OrderBy(q => q.DisplayOrder)
            .Select(q => (q.Code, q.DisplayOrder))
            .Should().Equal(("PR_FUNDING_TIME", 0), ("PR_ROLE_DURATION", 1));
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
        envelope.AnswerErrors.Should().Contain(e => e.QuestionCode == "NA_PLAN");

        // The conditional follow-up is not revealed while NA_WHO_WITH is unanswered, so it must not be enforced.
        envelope.AnswerErrors.Should().NotContain(e => e.QuestionCode == "NA_WHO_WITH_OTHER");

        // And NA_SHARED is not part of this category's step at all (BLUM-7759), so it can never be demanded.
        envelope.AnswerErrors.Should().NotContain(e => e.QuestionCode == "NA_SHARED");
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
        answers.Should().NotContain(a => a.QuestionCode == "NA_SHARED");
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
            new ActivityAnswerInputDto("NA_PLAN", "Run a local needs assessment"),
        ];
        var response = await Client.PutAsJsonAsync(
            $"activities/{dto.Id}",
            new UpdateActivityRequestDto("Renamed", categoryId, taskId, null, "planned", null, null, false, CustomResults(), resent));

        response.IsSuccessStatusCode.Should().BeTrue("resending every answer satisfies the edit: {0}", await response.Content.ReadAsStringAsync());
        var stored = await Context.ActivityAnswers.AsNoTracking().Where(a => a.ActivityId == new ActivityId(dto.Id)).ToListAsync();
        stored.Should().ContainSingle(a => a.QuestionCode == "NA_WHO_WITH" && a.Value == "police");
    }

    // "What is the name of this activity?" is a structural field outside the schema, and the 13 August Question
    // Protocol made it mandatory on completion (1.BCDEF.1.1, BLUM-7620) — it was optional under BLUM-7041. The
    // refusal has its own envelope so the client can put the message on the field rather than the page banner.
    [Fact]
    public async Task ShouldRejectCompletionWithoutAName_EvenWhenActivityDetailsAreAnswered()
    {
        AuthenticateAsMainOrgLead();

        var response = await PostActivityAsync(CompleteAnswers(), title: null);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("ACTIVITY_NAME_REQUIRED");
        body.Should().Contain("Enter the name of the activity");
        (await Context.Activities.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    // A draft may still be saved without one — the rule sits inside the handler's completion block.
    [Fact]
    public async Task ShouldSaveDraftWithoutAName()
    {
        AuthenticateAsMainOrgLead();

        var response = await PostActivityAsync(CompleteAnswers(), saveAsDraft: true, title: null);

        response.IsSuccessStatusCode.Should().BeTrue(
            "the name is only required on completion: {0}", await response.Content.ReadAsStringAsync());
        var dto = await response.ToResponseModel<ActivityDto>();
        (await Context.Activities.AsNoTracking().FirstAsync(x => x.Id == new ActivityId(dto.Id))).IsComplete.Should().BeFalse();
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

        // Only ShouldNeitherAskNorClearTheDeactivatedSharedAuthorityQuestion adds this row; the fresh seed has none.
        // Its options cascade with it.
        await Context.FormQuestions.Where(q => q.Code == "ST_SHARED").ExecuteDeleteAsync();
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