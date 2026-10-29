using System.Net.Http.Json;
using Api.Dtos;
using Application.Common.Features;
using FluentAssertions;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.FormDesigner;

// Shared setup for Form Designer authoring tests. The activity-record form schema is global (not org-scoped)
// and is seeded on startup, so each test starts from a cleared schema for deterministic assertions.
// The designer's endpoints answer only while BLUM_9395 is on, so every test starts with it on.
public abstract class FormDesignerTestBase(IntegrationTestWebFactory factory)
    : BaseIntegrationTest(factory), IAsyncLifetime
{
    protected Task SetFlagAsync(bool enabled) => factory.SetFeatureAsync(Feature.BLUM_9395, enabled);

    public async Task InitializeAsync()
    {
        await SetFlagAsync(true);
        AuthenticateAsAdmin();
        await ClearSchemaAsync();
    }

    public async Task DisposeAsync()
    {
        await ClearSchemaAsync();
        AuthenticateAsAdmin();
    }

    protected async Task ClearSchemaAsync()
    {
        Context.ActivityAnswers.RemoveRange(Context.ActivityAnswers);
        Context.FormQuestionOptions.RemoveRange(Context.FormQuestionOptions);
        Context.FormQuestions.RemoveRange(Context.FormQuestions);
        Context.FormSections.RemoveRange(Context.FormSections);
        await SaveChangesAsync();
    }

    protected async Task<FormSectionDto> CreateSectionAsync(string title = "Activity details", string? description = null)
    {
        var response = await Client.PostAsJsonAsync(
            "admin/activity-form/sections",
            new CreateSectionRequestDto(title, description));
        response.IsSuccessStatusCode.Should().BeTrue();
        return await response.ToResponseModel<FormSectionDto>();
    }

    protected async Task<FormQuestionDto> CreateQuestionAsync(CreateQuestionRequestDto request)
    {
        var response = await Client.PostAsJsonAsync("admin/activity-form/questions", request);
        response.IsSuccessStatusCode.Should().BeTrue();
        return await response.ToResponseModel<FormQuestionDto>();
    }

    protected async Task<FormSchemaDto> GetSchemaAsync()
    {
        var response = await Client.GetAsync("admin/activity-form/schema");
        response.EnsureSuccessStatusCode();
        return await response.ToResponseModel<FormSchemaDto>();
    }

    protected static CreateQuestionRequestDto TextQuestion(Guid sectionId, string code, string label = "A question") =>
        new(sectionId, code, label, "Text", false, null, null, null, null, null, null);

    protected static CreateQuestionRequestDto RadioQuestion(Guid sectionId, string code, string label = "Pick one") =>
        new(
            sectionId,
            code,
            label,
            "Radio",
            true,
            null,
            null,
            null,
            null,
            null,
            new List<QuestionOptionRequestDto>
            {
                new("yes", "Yes", 0),
                new("no", "No", 1),
            });
}