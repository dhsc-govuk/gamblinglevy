using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
using Domain.Forms;
using Domain.Users;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.Activities;

// Dynamic-question answers submitted with an activity: persistence, required + conditional validation, checkbox
// round-trip, and answer survival when a question is later deleted (BLUM-6444). The seeded section is global
// (no category) so it applies to whichever category the test activity uses.
public class ActivityAnswerTests(IntegrationTestWebFactory factory) : ActivityTestBase(factory)
{
    // Seeds a deterministic global schema: a required radio (Q_METHOD), a conditional-required text revealed only
    // when Q_METHOD == online (Q_PLATFORM), and an optional checkbox (Q_TOPICS).
    private async Task SeedSchemaAsync()
    {
        AuthenticateAsAdmin();
        await ClearFormSchemaAsync();

        var actor = new UserId(Guid.NewGuid());
        var section = FormSection.Create(FormSectionId.New(), "Details", 0, actor, DateTime.UtcNow);

        var method = FormQuestion.Create(
            FormQuestionId.New(), section.Id, "Q_METHOD", "Method?", QuestionType.Radio, 0, actor, isRequired: true, utcNow: DateTime.UtcNow);
        method.AddOption(FormQuestionOptionId.New(), "online", "Online", 0, DateTime.UtcNow);
        method.AddOption(FormQuestionOptionId.New(), "in_person", "In person", 1, DateTime.UtcNow);

        var platform = FormQuestion.Create(
            FormQuestionId.New(),
            section.Id,
            "Q_PLATFORM",
            "Platform?",
            QuestionType.Text,
            1,
            actor,
            isRequired: true,
            conditionalQuestionCode: "Q_METHOD",
            conditionalValue: "online",
            utcNow: DateTime.UtcNow);

        var topics = FormQuestion.Create(
            FormQuestionId.New(), section.Id, "Q_TOPICS", "Topics", QuestionType.Checkbox, 2, actor, isRequired: false, utcNow: DateTime.UtcNow);
        topics.AddOption(FormQuestionOptionId.New(), "a", "A", 0, DateTime.UtcNow);
        topics.AddOption(FormQuestionOptionId.New(), "b", "B", 1, DateTime.UtcNow);
        topics.AddOption(FormQuestionOptionId.New(), "c", "C", 2, DateTime.UtcNow);

        Context.FormSections.Add(section);
        Context.FormQuestions.AddRange(method, platform, topics);
        await SaveChangesAsync();
    }

    private async Task<HttpResponseMessage> PostActivityAsync(IReadOnlyList<ActivityAnswerInputDto>? answers, bool saveAsDraft)
    {
        var (categoryId, taskId) = await GetCategoryAndTypeAsync();
        var status = saveAsDraft ? null : "planned";
        return await Client.PostAsJsonAsync(
            "activities",
            new CreateActivityRequestDto(
                "Answer test", categoryId, taskId, null, status, null, null, saveAsDraft, saveAsDraft ? null : DefaultExpectedResults, answers));
    }

    [Fact]
    public async Task ShouldPersistAnswersAndReturnThemOnGet()
    {
        await SeedSchemaAsync();
        AuthenticateAsMainOrgLead();

        var answers = new List<ActivityAnswerInputDto>
        {
            new("Q_METHOD", "online"),
            new("Q_PLATFORM", "Zoom"),
            new("Q_TOPICS", "a,c"),
        };
        var response = await PostActivityAsync(answers, saveAsDraft: false);
        response.IsSuccessStatusCode.Should().BeTrue();
        var created = await response.ToResponseModel<ActivityDto>();

        var get = await Client.GetAsync($"activities/{created.Id}");
        get.EnsureSuccessStatusCode();
        var dto = await get.ToResponseModel<ActivityDto>();

        dto.Answers.Should().Contain(a => a.QuestionCode == "Q_METHOD" && a.Value == "online");
        dto.Answers.Should().Contain(a => a.QuestionCode == "Q_PLATFORM" && a.Value == "Zoom");
        dto.Answers.Should().Contain(a => a.QuestionCode == "Q_TOPICS" && a.Value == "a,c");
    }

    [Fact]
    public async Task ShouldRejectCompleteActivityMissingRequiredAnswer()
    {
        await SeedSchemaAsync();
        AuthenticateAsMainOrgLead();

        var response = await PostActivityAsync(new List<ActivityAnswerInputDto>(), saveAsDraft: false);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task ShouldAllowDraftMissingRequiredAnswer()
    {
        await SeedSchemaAsync();
        AuthenticateAsMainOrgLead();

        var response = await PostActivityAsync(new List<ActivityAnswerInputDto>(), saveAsDraft: true);

        response.IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldNotRequireHiddenConditionalAnswer()
    {
        await SeedSchemaAsync();
        AuthenticateAsMainOrgLead();

        // Q_PLATFORM is required only when Q_METHOD == online; choosing in_person hides it.
        var answers = new List<ActivityAnswerInputDto> { new("Q_METHOD", "in_person") };
        var response = await PostActivityAsync(answers, saveAsDraft: false);

        response.IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldRequireVisibleConditionalAnswer()
    {
        await SeedSchemaAsync();
        AuthenticateAsMainOrgLead();

        // Q_METHOD == online makes Q_PLATFORM visible and required, but it is omitted.
        var answers = new List<ActivityAnswerInputDto> { new("Q_METHOD", "online") };
        var response = await PostActivityAsync(answers, saveAsDraft: false);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task ShouldPreserveAnswerWhenQuestionDeleted()
    {
        await SeedSchemaAsync();
        AuthenticateAsMainOrgLead();

        var answers = new List<ActivityAnswerInputDto>
        {
            new("Q_METHOD", "in_person"),
            new("Q_TOPICS", "b"),
        };
        var response = await PostActivityAsync(answers, saveAsDraft: false);
        response.IsSuccessStatusCode.Should().BeTrue();
        var created = await response.ToResponseModel<ActivityDto>();

        // Hard-delete the topics question; its answer must survive (id-only reference, snapshotted code/value).
        AuthenticateAsAdmin();
        var topics = await Context.FormQuestions.Include(q => q.Options).FirstAsync(q => q.Code == "Q_TOPICS");
        Context.FormQuestions.Remove(topics);
        await SaveChangesAsync();

        AuthenticateAsMainOrgLead();
        var get = await Client.GetAsync($"activities/{created.Id}");
        get.EnsureSuccessStatusCode();
        var dto = await get.ToResponseModel<ActivityDto>();

        dto.Answers.Should().Contain(a => a.QuestionCode == "Q_TOPICS" && a.Value == "b");
    }
}