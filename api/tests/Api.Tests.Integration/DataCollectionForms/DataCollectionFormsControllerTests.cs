using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
using Domain.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.DataCollectionForms;

public class DataCollectionFormsControllerTests : BaseIntegrationTest, IAsyncLifetime
{
    private const string Route = "admin/data-collection-forms";

    public DataCollectionFormsControllerTests(IntegrationTestWebFactory factory)
        : base(factory)
    {
    }

    private sealed record ModuleResult(Guid Id, string Code, string Name, string Status);

    private sealed record SectionResult(Guid Id, string Title, int SectionNumber);

    private sealed record OptionResult(string Value, string Label);

    private sealed record QuestionResult(Guid Id, string Code, string Label, string QuestionType, List<OptionResult> Options);

    private sealed record ModuleWithChildrenResult(Guid Id, string Code, string Status, List<SectionResult> Sections, List<QuestionResult> Questions);

    public async Task InitializeAsync()
    {
        if (!await Context.Users.AsNoTracking().AnyAsync(u => u.Id == AdminsData.MainAdmin.Id))
        {
            await Context.Users.AddAsync(AdminsData.MainAdmin);
            await SaveChangesAsync();
        }

        AuthenticateAsAdmin();
    }

    public Task DisposeAsync()
    {
        AuthenticateAsAdmin();
        return Task.CompletedTask;
    }

    private async Task<Guid> CreateModuleAsync(string code)
    {
        var response = await Client.PostAsJsonAsync($"{Route}/modules", new CreateFormModuleRequestDto(code, $"Module {code}", "desc"));
        response.IsSuccessStatusCode.Should().BeTrue();
        return (await response.ToResponseModel<ModuleResult>()).Id;
    }

    private async Task<ModuleWithChildrenResult> GetModuleAsync(Guid moduleId)
    {
        var response = await Client.GetAsync($"{Route}/modules/{moduleId}");
        response.IsSuccessStatusCode.Should().BeTrue();
        return await response.ToResponseModel<ModuleWithChildrenResult>();
    }

    [Fact]
    public async Task GetModules_ReturnsTheSeededModules()
    {
        var response = await Client.GetAsync($"{Route}/modules");

        response.IsSuccessStatusCode.Should().BeTrue();
        var modules = await response.ToResponseModel<List<ModuleResult>>();
        modules.Select(m => m.Code).Should().Contain(new[] { "activity_key_results", "funding_spent", "strategic_feedback" });
    }

    [Fact]
    public async Task CreateModule_ThenGetWithChildren_ReturnsModuleWithEmptyChildren()
    {
        var moduleId = await CreateModuleAsync("test_module_children");

        var module = await GetModuleAsync(moduleId);

        module.Id.Should().Be(moduleId);
        module.Sections.Should().BeEmpty();
        module.Questions.Should().BeEmpty();
        module.Status.Should().Be("draft");
    }

    [Fact]
    public async Task CreateSection_AddsSectionToModule()
    {
        var moduleId = await CreateModuleAsync("test_section_module");

        var response = await Client.PostAsJsonAsync(
            $"{Route}/modules/{moduleId}/sections",
            new CreateFormSectionRequestDto("Reporting details", "Section description", null));
        response.IsSuccessStatusCode.Should().BeTrue();

        var module = await GetModuleAsync(moduleId);
        module.Sections.Should().ContainSingle(s => s.Title == "Reporting details");
    }

    [Fact]
    public async Task CreateRadioQuestion_WithOptions_AddsQuestionAndOptions()
    {
        var moduleId = await CreateModuleAsync("test_question_module");

        var request = new CreateFormQuestionRequestDto(
            SectionId: null,
            Code: "Q1",
            Label: "Did you deliver the activity?",
            QuestionType: "Radio",
            IsRequired: true,
            Hint: null,
            HelpText: null,
            Options: new[]
            {
                new FormModuleQuestionOptionRequestDto("yes", "Yes"),
                new FormModuleQuestionOptionRequestDto("no", "No"),
            });

        var response = await Client.PostAsJsonAsync($"{Route}/modules/{moduleId}/questions", request);
        response.IsSuccessStatusCode.Should().BeTrue();

        var module = await GetModuleAsync(moduleId);
        var question = module.Questions.Should().ContainSingle(q => q.Code == "Q1").Which;
        question.QuestionType.Should().Be("Radio");
        question.Options.Should().HaveCount(2);
        question.Options.Select(o => o.Value).Should().Contain(new[] { "yes", "no" });
    }

    [Fact]
    public async Task PublishModule_WithQuestion_SetsPublishedStatus()
    {
        var moduleId = await CreateModuleAsync("test_publish_module");
        var question = new CreateFormQuestionRequestDto(
            SectionId: null,
            Code: "Q1",
            Label: "Number of sessions",
            QuestionType: "Number",
            IsRequired: true,
            Hint: null,
            HelpText: null,
            Options: null);
        (await Client.PostAsJsonAsync($"{Route}/modules/{moduleId}/questions", question)).IsSuccessStatusCode.Should().BeTrue();

        var response = await Client.PostAsync($"{Route}/modules/{moduleId}/publish", null);

        response.IsSuccessStatusCode.Should().BeTrue();
        var module = await GetModuleAsync(moduleId);
        module.Status.Should().Be("published");
    }

    [Fact]
    public async Task DeleteSection_WithQuestionAssigned_ReturnsUnprocessableEntity()
    {
        // Regression #4: a section that still has questions assigned to it cannot be deleted — it must
        // surface as 422 Unprocessable Entity rather than orphaning the questions.
        var moduleId = await CreateModuleAsync("test_delete_section_with_question");

        var sectionResponse = await Client.PostAsJsonAsync(
            $"{Route}/modules/{moduleId}/sections",
            new CreateFormSectionRequestDto("Section with question", "desc", null));
        sectionResponse.IsSuccessStatusCode.Should().BeTrue();

        var sectionId = (await GetModuleAsync(moduleId)).Sections.Single().Id;

        var question = new CreateFormQuestionRequestDto(
            SectionId: sectionId,
            Code: "Q1",
            Label: "Number of sessions",
            QuestionType: "Number",
            IsRequired: true,
            Hint: null,
            HelpText: null,
            Options: null);
        (await Client.PostAsJsonAsync($"{Route}/modules/{moduleId}/questions", question)).IsSuccessStatusCode.Should().BeTrue();

        var response = await Client.DeleteAsync($"{Route}/modules/{moduleId}/sections/{sectionId}");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task UpdateQuestion_OnPublishedModule_ReturnsUnprocessableEntity()
    {
        // Regression #6: editing a question on a published (locked) module must fail gracefully as 422
        // Unprocessable Entity rather than throwing an unhandled 500.
        var moduleId = await CreateModuleAsync("test_update_published_question");

        var createQuestion = new CreateFormQuestionRequestDto(
            SectionId: null,
            Code: "Q1",
            Label: "Number of sessions",
            QuestionType: "Number",
            IsRequired: true,
            Hint: null,
            HelpText: null,
            Options: null);
        (await Client.PostAsJsonAsync($"{Route}/modules/{moduleId}/questions", createQuestion)).IsSuccessStatusCode.Should().BeTrue();

        var questionId = (await GetModuleAsync(moduleId)).Questions.Single().Id;

        (await Client.PostAsync($"{Route}/modules/{moduleId}/publish", null)).IsSuccessStatusCode.Should().BeTrue();

        var update = new UpdateFormQuestionRequestDto(
            SectionId: null,
            Label: "Updated label",
            QuestionType: "Number",
            IsRequired: true,
            Hint: null,
            HelpText: null,
            Options: null);

        var response = await Client.PutAsJsonAsync($"{Route}/modules/{moduleId}/questions/{questionId}", update);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task AddSection_AfterDeletingAnEarlierSection_DoesNotReuseSectionNumber()
    {
        // Regression #13: section numbers must stay unique after a deletion (max+1, not count+1).
        var moduleId = await CreateModuleAsync("test_section_numbering");
        (await Client.PostAsJsonAsync($"{Route}/modules/{moduleId}/sections", new CreateFormSectionRequestDto("Section A", null, null))).IsSuccessStatusCode.Should().BeTrue();
        (await Client.PostAsJsonAsync($"{Route}/modules/{moduleId}/sections", new CreateFormSectionRequestDto("Section B", null, null))).IsSuccessStatusCode.Should().BeTrue();

        var sectionAId = (await GetModuleAsync(moduleId)).Sections.Single(s => s.Title == "Section A").Id;
        (await Client.DeleteAsync($"{Route}/modules/{moduleId}/sections/{sectionAId}")).IsSuccessStatusCode.Should().BeTrue();

        (await Client.PostAsJsonAsync($"{Route}/modules/{moduleId}/sections", new CreateFormSectionRequestDto("Section C", null, null))).IsSuccessStatusCode.Should().BeTrue();

        var sections = (await GetModuleAsync(moduleId)).Sections;
        sections.Should().HaveCount(2);
        sections.Select(s => s.SectionNumber).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task GetModules_AsNonAdmin_IsForbidden()
    {
        AuthenticateAs(Role.Lead, Guid.NewGuid(), Guid.NewGuid());

        var response = await Client.GetAsync($"{Route}/modules");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}