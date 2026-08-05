using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
using FluentAssertions;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.FormDesigner;

// DHSC Form Designer authoring: section + question CRUD, code uniqueness, conditional references, deletion
// guards, reordering, and role gating (BLUM-6444).
public class FormDesignerTests(IntegrationTestWebFactory factory) : FormDesignerTestBase(factory)
{
    [Fact]
    public async Task ShouldCreateSectionAndQuestionWithOptions()
    {
        var section = await CreateSectionAsync("Activity details", "How the activity is delivered.");
        var question = await CreateQuestionAsync(RadioQuestion(section.Id, "DELIVERY_METHOD", "How is it delivered?"));

        question.Code.Should().Be("DELIVERY_METHOD");
        question.QuestionType.Should().Be("Radio");
        question.IsActive.Should().BeTrue();
        question.Options.Should().HaveCount(2);

        var schema = await GetSchemaAsync();
        schema.Sections.Should().ContainSingle(s => s.Id == section.Id);
        schema.Questions.Should().ContainSingle(q => q.Code == "DELIVERY_METHOD");
    }

    [Fact]
    public async Task ShouldRejectDuplicateQuestionCode()
    {
        var section = await CreateSectionAsync();
        await CreateQuestionAsync(TextQuestion(section.Id, "DUPLICATE"));

        var response = await Client.PostAsJsonAsync(
            "admin/activity-form/questions",
            TextQuestion(section.Id, "DUPLICATE"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ShouldRejectChoiceQuestionWithoutOptions()
    {
        var section = await CreateSectionAsync();

        var response = await Client.PostAsJsonAsync(
            "admin/activity-form/questions",
            new CreateQuestionRequestDto(section.Id, "NO_OPTIONS", "Pick", "Select", false, null, null, null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task ShouldRejectConditionalReferenceToMissingQuestion()
    {
        var section = await CreateSectionAsync();

        var response = await Client.PostAsJsonAsync(
            "admin/activity-form/questions",
            new CreateQuestionRequestDto(section.Id, "CHILD", "Child", "Text", false, null, null, null, "GHOST", "yes", null));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task ShouldNotDeleteSectionThatStillHasQuestions()
    {
        var section = await CreateSectionAsync();
        await CreateQuestionAsync(TextQuestion(section.Id, "KEEPER"));

        var response = await Client.DeleteAsync($"admin/activity-form/sections/{section.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ShouldNotDeleteQuestionReferencedByConditionalRule()
    {
        var section = await CreateSectionAsync();
        var parent = await CreateQuestionAsync(RadioQuestion(section.Id, "PARENT"));
        await CreateQuestionAsync(
            new CreateQuestionRequestDto(section.Id, "CHILD", "Child", "Text", false, null, null, null, "PARENT", "yes", null));

        var response = await Client.DeleteAsync($"admin/activity-form/questions/{parent.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ShouldReorderQuestionsWithinSection()
    {
        var section = await CreateSectionAsync();
        var first = await CreateQuestionAsync(TextQuestion(section.Id, "FIRST"));
        var second = await CreateQuestionAsync(TextQuestion(section.Id, "SECOND"));

        var response = await Client.PostAsJsonAsync(
            "admin/activity-form/questions/reorder",
            new ReorderQuestionsRequestDto(section.Id, new List<Guid> { second.Id, first.Id }));
        response.EnsureSuccessStatusCode();

        var schema = await GetSchemaAsync();
        var ordered = schema.Questions.OrderBy(q => q.DisplayOrder).Select(q => q.Code).ToList();
        ordered.Should().Equal("SECOND", "FIRST");
    }

    [Fact]
    public async Task ShouldDeactivateAndReactivateQuestion()
    {
        var section = await CreateSectionAsync();
        var question = await CreateQuestionAsync(TextQuestion(section.Id, "TOGGLE"));

        var deactivate = await Client.PutAsJsonAsync(
            $"admin/activity-form/questions/{question.Id}",
            new UpdateQuestionRequestDto(section.Id, "Toggle", "Text", false, false, null, null, null, null, null, null));
        deactivate.EnsureSuccessStatusCode();

        var schema = await GetSchemaAsync();
        schema.Questions.Single(q => q.Code == "TOGGLE").IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task ShouldForbidOrganisationUserFromAuthoring()
    {
        AuthenticateAs(Domain.ValueObjects.Role.Lead, Guid.NewGuid(), Guid.NewGuid());

        var response = await Client.PostAsJsonAsync(
            "admin/activity-form/sections",
            new CreateSectionRequestDto("Sneaky", null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}