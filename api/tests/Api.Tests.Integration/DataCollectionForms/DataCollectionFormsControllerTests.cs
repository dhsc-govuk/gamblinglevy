using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Dtos;
using Domain.AuditLogs;
using Domain.DataCollectionForms;
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

    // BLUM-8747: creating a module is audited against the module, as a DataCollectionForm target.
    [Fact]
    public async Task CreateModule_WritesOneModuleCreatedAuditRow()
    {
        var moduleId = await CreateModuleAsync("test_module_audited");

        var rows = await Context.AuditLogs.AsNoTracking()
            .Where(a => a.TargetId == moduleId)
            .ToListAsync();
        var row = rows.Should().ContainSingle().Subject;
        row.Action.Should().Be(AuditAction.DataCollectionFormModuleCreated);
        row.TargetType.Should().Be(AuditTargetType.DataCollectionForm);
        row.ActorUserId.Should().Be(AdminsData.MainAdmin.Id);
        row.After.Should().Contain("test_module_audited");
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
    public async Task UpdateModule_RenamesTheModule_AndWritesAnAuditRow()
    {
        var moduleId = await CreateModuleAsync("test_update_module");

        var response = await Client.PutAsJsonAsync(
            $"{Route}/modules/{moduleId}",
            new UpdateFormModuleRequestDto("Renamed module", "A new description"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var module = await response.ToResponseModel<FormModuleWithChildrenDto>();
        module.Name.Should().Be("Renamed module");
        module.Description.Should().Be("A new description");
        module.Code.Should().Be("test_update_module", "the code is the module's identity and is not editable");

        (await GetModuleTreeAsync(moduleId)).Name.Should().Be("Renamed module");

        var row = await AuditRowAsync(moduleId, AuditAction.DataCollectionFormModuleUpdated);
        row.ActorUserId.Should().Be(AdminsData.MainAdmin.Id);
        Property(row.Before, "name").GetString().Should().Be("Module test_update_module");
        Property(row.After, "name").GetString().Should().Be("Renamed module");
    }

    [Fact]
    public async Task DeleteModule_ArchivesTheModule_AndWritesAnAuditRow()
    {
        var moduleId = await CreateModuleAsync("test_delete_module");

        var response = await Client.DeleteAsync($"{Route}/modules/{moduleId}");

        // "Delete" retires the module rather than removing it: collections may already be built on it.
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();

        (await Context.FormModules.AsNoTracking().AnyAsync(x => x.Id == new FormModuleId(moduleId))).Should().BeTrue();
        (await GetModuleTreeAsync(moduleId)).Status.Should().Be("archived");

        var row = await AuditRowAsync(moduleId, AuditAction.DataCollectionFormModuleDeleted);
        Property(row.Before, "status").GetString().Should().Be("draft");
        row.After.Should().BeNull();
    }

    [Fact]
    public async Task UpdateSection_ChangesItsText_AndWritesAnAuditRow()
    {
        var moduleId = await CreateModuleAsync("test_update_section");
        var first = await CreateSectionAsync(moduleId, "First section");
        var second = await CreateSectionAsync(moduleId, "Original title");

        var response = await Client.PutAsJsonAsync(
            $"{Route}/modules/{moduleId}/sections/{second.Id}",
            new UpdateFormSectionRequestDto("New title", "New description", "New help text"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var module = await response.ToResponseModel<FormModuleWithChildrenDto>();

        // The edit changes the text only: the section keeps its number, and its neighbour is untouched.
        module.Sections.Should().BeEquivalentTo(
        [
            first,
            second with { Title = "New title", Description = "New description", HelpText = "New help text" },
        ]);

        var row = await AuditRowAsync(second.Id, AuditAction.DataCollectionFormSectionUpdated);
        Property(row.Before, "title").GetString().Should().Be("Original title");
        Property(row.After, "title").GetString().Should().Be("New title");
        Property(row.After, "helpText").GetString().Should().Be("New help text");
    }

    [Fact]
    public async Task DeleteQuestion_RemovesItWithItsOptions_AndWritesAnAuditRow()
    {
        var moduleId = await CreateModuleAsync("test_delete_question");
        var doomed = await CreateQuestionAsync(moduleId, "DOOMED", "Radio", options: YesNo);
        var survivor = await CreateQuestionAsync(moduleId, "SURVIVOR", "Number");

        var response = await Client.DeleteAsync($"{Route}/modules/{moduleId}/questions/{doomed.Id}");

        // Unlike the module, a question is really deleted, and the answer is the module without it.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var module = await response.ToResponseModel<FormModuleWithChildrenDto>();
        module.Questions.Select(q => q.Id).Should().Equal(survivor.Id);

        var doomedId = new DcFormQuestionId(doomed.Id);
        (await Context.DcFormQuestions.AsNoTracking().AnyAsync(x => x.Id == doomedId)).Should().BeFalse();
        (await Context.DcFormQuestionOptions.AsNoTracking().AnyAsync(x => x.QuestionId == doomedId)).Should().BeFalse();

        var row = await AuditRowAsync(doomed.Id, AuditAction.DataCollectionFormQuestionDeleted);
        Property(row.Before, "code").GetString().Should().Be("DOOMED");
        Property(row.Before, "options").GetArrayLength().Should().Be(2);
        row.After.Should().BeNull();
    }

    [Fact]
    public async Task UpdateQuestion_OnADraftModule_ReplacesItsOptions_AndWritesAnAuditRow()
    {
        var moduleId = await CreateModuleAsync("test_update_draft_question");
        var section = await CreateSectionAsync(moduleId, "A section");
        var question = await CreateQuestionAsync(moduleId, "Q1", "Radio", options: YesNo);

        var update = new UpdateFormQuestionRequestDto(
            SectionId: section.Id,
            Label: "Reworded question",
            QuestionType: "Select",
            IsRequired: false,
            Hint: "A hint",
            HelpText: null,
            Options: new[]
            {
                new FormModuleQuestionOptionRequestDto("a", "A"),
                new FormModuleQuestionOptionRequestDto("b", "B"),
                new FormModuleQuestionOptionRequestDto("c", "C"),
            });

        var response = await Client.PutAsJsonAsync($"{Route}/modules/{moduleId}/questions/{question.Id}", update);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await response.ToResponseModel<FormModuleWithChildrenDto>()).Questions.Should().ContainSingle().Subject;

        updated.Id.Should().Be(question.Id);
        updated.Code.Should().Be("Q1", "saved answers are keyed on the code, so an edit never changes it");
        updated.Label.Should().Be("Reworded question");
        updated.QuestionType.Should().Be("Select");
        updated.IsRequired.Should().BeFalse();
        updated.Hint.Should().Be("A hint");
        updated.SectionId.Should().Be(section.Id);

        // The option list is replaced, not merged, and numbered by position from 1.
        updated.Options.Select(o => o.Value).Should().Equal("a", "b", "c");
        updated.Options.Select(o => o.DisplayOrder).Should().Equal(1, 2, 3);

        var row = await AuditRowAsync(question.Id, AuditAction.DataCollectionFormQuestionUpdated);
        Property(row.Before, "label").GetString().Should().Be("Question Q1");
        Property(row.Before, "options").GetArrayLength().Should().Be(2);
        Property(row.After, "label").GetString().Should().Be("Reworded question");
        Property(row.After, "options").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task UpdateQuestion_KeepsAWithdrawnOptionWithdrawn_AndAddsANewOptionActive()
    {
        var moduleId = await CreateModuleAsync("test_update_withdrawn_option");
        var question = await CreateQuestionAsync(
            moduleId,
            "Q1",
            "Radio",
            options: [new("a", "A"), new("b", "B")]);

        // Nothing in the API withdraws an option yet; a restored version can carry one that is.
        await Context.Database.ExecuteSqlRawAsync(
            "UPDATE dc_form_question_options SET is_active = false WHERE question_id = {0} AND value = 'b'", question.Id);

        var update = new UpdateFormQuestionRequestDto(
            SectionId: null,
            Label: "Question Q1",
            QuestionType: "Radio",
            IsRequired: true,
            Hint: null,
            HelpText: null,
            Options: new[]
            {
                new FormModuleQuestionOptionRequestDto("a", "A"),
                new FormModuleQuestionOptionRequestDto("b", "B reworded"),
                new FormModuleQuestionOptionRequestDto("c", "C"),
            });

        var response = await Client.PutAsJsonAsync($"{Route}/modules/{moduleId}/questions/{question.Id}", update);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // The request carries no IsActive, so each value keeps the state it had and only a new value starts active.
        var options = await Context.DcFormQuestionOptions.AsNoTracking()
            .Where(o => o.QuestionId == new DcFormQuestionId(question.Id))
            .OrderBy(o => o.DisplayOrder)
            .ToListAsync();
        options.Select(o => (o.Value, o.Label, o.DisplayOrder, o.IsActive)).Should().Equal(
            ("a", "A", 1, true),
            ("b", "B reworded", 2, false),
            ("c", "C", 3, true));
    }

    [Fact]
    public async Task ReorderSections_RenumbersFromOne_AndWritesAnAuditRow()
    {
        var moduleId = await CreateModuleAsync("test_reorder_sections");
        var a = await CreateSectionAsync(moduleId, "Section A");
        var b = await CreateSectionAsync(moduleId, "Section B");
        var c = await CreateSectionAsync(moduleId, "Section C");

        var response = await Client.PostAsJsonAsync(
            $"{Route}/modules/{moduleId}/sections/reorder",
            new ReorderRequestDto(new List<Guid> { c.Id, a.Id, b.Id }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var module = await response.ToResponseModel<FormModuleWithChildrenDto>();
        module.Sections.Select(s => s.Id).Should().Equal(c.Id, a.Id, b.Id);
        module.Sections.Select(s => s.SectionNumber).Should().Equal(1, 2, 3);
        (await GetModuleTreeAsync(moduleId)).Sections.Select(s => s.Id).Should().Equal(c.Id, a.Id, b.Id);

        // The reorder is audited against the module: the order is the module's, not any one section's.
        var row = await AuditRowAsync(moduleId, AuditAction.DataCollectionFormSectionsReordered);
        OrderedIds(row.Before).Should().Equal(a.Id, b.Id, c.Id);
        OrderedIds(row.After).Should().Equal(c.Id, a.Id, b.Id);
    }

    [Fact]
    public async Task ReorderQuestions_RenumbersFromOne_AndWritesAnAuditRow()
    {
        var moduleId = await CreateModuleAsync("test_reorder_questions");
        var a = await CreateQuestionAsync(moduleId, "QA", "Number");
        var b = await CreateQuestionAsync(moduleId, "QB", "Number");
        var c = await CreateQuestionAsync(moduleId, "QC", "Number");

        var response = await Client.PostAsJsonAsync(
            $"{Route}/modules/{moduleId}/questions/reorder",
            new ReorderRequestDto(new List<Guid> { c.Id, a.Id, b.Id }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var module = await response.ToResponseModel<FormModuleWithChildrenDto>();
        module.Questions.Select(q => q.Id).Should().Equal(c.Id, a.Id, b.Id);
        module.Questions.Select(q => q.DisplayOrder).Should().Equal(1, 2, 3);
        (await GetModuleTreeAsync(moduleId)).Questions.Select(q => q.Id).Should().Equal(c.Id, a.Id, b.Id);

        var row = await AuditRowAsync(moduleId, AuditAction.DataCollectionFormQuestionsReordered);
        OrderedIds(row.Before).Should().Equal(a.Id, b.Id, c.Id);
        OrderedIds(row.After).Should().Equal(c.Id, a.Id, b.Id);
    }

    [Theory]
    [InlineData("sections", "SECTION_NOT_FOUND")]
    [InlineData("questions", "QUESTION_NOT_FOUND")]
    public async Task Reorder_NamingAnIdOutsideTheModule_ReturnsNotFound_AndLeavesTheOrderUntouched(string children, string errorCode)
    {
        var moduleId = await CreateModuleAsync($"test_reorder_unknown_{children}");
        var (a, b) = children == "sections"
            ? ((await CreateSectionAsync(moduleId, "Section A")).Id, (await CreateSectionAsync(moduleId, "Section B")).Id)
            : ((await CreateQuestionAsync(moduleId, "QA", "Number")).Id, (await CreateQuestionAsync(moduleId, "QB", "Number")).Id);

        var response = await Client.PostAsJsonAsync(
            $"{Route}/modules/{moduleId}/{children}/reorder",
            new ReorderRequestDto(new List<Guid> { b, Guid.NewGuid(), a }));

        // Refused as a whole. The activity form designer skips an unknown id and applies the rest
        // (FormDesignerTests.ShouldIgnoreUnknownIds_WhenReorderingSections); this builder does not.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ErrorCodeAsync(response)).Should().Be(errorCode);

        var module = await GetModuleTreeAsync(moduleId);
        var order = children == "sections" ? module.Sections.Select(s => s.Id) : module.Questions.Select(q => q.Id);
        order.Should().Equal(a, b);
    }

    [Theory]
    [InlineData("reorder-sections")]
    [InlineData("reorder-questions")]
    [InlineData("update-section")]
    [InlineData("delete-question")]
    public async Task StructuralEdit_OnAPublishedModule_ReturnsUnprocessableEntity_AndChangesNothing(string edit)
    {
        // Publishing locks a module's structure: collections are built on it and answers saved against it.
        var moduleId = await CreateModuleAsync($"test_locked_{edit.Replace('-', '_')}");
        var sectionA = await CreateSectionAsync(moduleId, "Section A");
        var sectionB = await CreateSectionAsync(moduleId, "Section B");
        var questionA = await CreateQuestionAsync(moduleId, "QA", "Number");
        var questionB = await CreateQuestionAsync(moduleId, "QB", "Number");
        (await Client.PostAsync($"{Route}/modules/{moduleId}/publish", null)).IsSuccessStatusCode.Should().BeTrue();
        var published = await GetModuleTreeAsync(moduleId);

        var response = edit switch
        {
            "reorder-sections" => await Client.PostAsJsonAsync(
                $"{Route}/modules/{moduleId}/sections/reorder",
                new ReorderRequestDto(new List<Guid> { sectionB.Id, sectionA.Id })),
            "reorder-questions" => await Client.PostAsJsonAsync(
                $"{Route}/modules/{moduleId}/questions/reorder",
                new ReorderRequestDto(new List<Guid> { questionB.Id, questionA.Id })),
            "update-section" => await Client.PutAsJsonAsync(
                $"{Route}/modules/{moduleId}/sections/{sectionA.Id}",
                new UpdateFormSectionRequestDto("Changed", null, null)),
            _ => await Client.DeleteAsync($"{Route}/modules/{moduleId}/questions/{questionA.Id}"),
        };

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorCodeAsync(response)).Should().Be("MODULE_NOT_EDITABLE");
        (await GetModuleTreeAsync(moduleId)).Should().BeEquivalentTo(published);
    }

    [Theory]
    [InlineData("update-module")]
    [InlineData("delete-module")]
    [InlineData("reorder-sections")]
    [InlineData("reorder-questions")]
    public async Task ModuleEdit_ForAnUnknownModule_ReturnsNotFound(string edit)
    {
        var moduleId = Guid.NewGuid();
        var anyOrder = new ReorderRequestDto(new List<Guid> { Guid.NewGuid() });

        var response = edit switch
        {
            "update-module" => await Client.PutAsJsonAsync($"{Route}/modules/{moduleId}", new UpdateFormModuleRequestDto("Name", null)),
            "delete-module" => await Client.DeleteAsync($"{Route}/modules/{moduleId}"),
            "reorder-sections" => await Client.PostAsJsonAsync($"{Route}/modules/{moduleId}/sections/reorder", anyOrder),
            _ => await Client.PostAsJsonAsync($"{Route}/modules/{moduleId}/questions/reorder", anyOrder),
        };

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ErrorCodeAsync(response)).Should().Be("MODULE_NOT_FOUND");
    }

    private static readonly FormModuleQuestionOptionRequestDto[] YesNo =
    [
        new("yes", "Yes"),
        new("no", "No"),
    ];

    // The API's own response shape, for the tests that assert on fields the trimmed records above leave out.
    private async Task<FormModuleWithChildrenDto> GetModuleTreeAsync(Guid moduleId)
    {
        var response = await Client.GetAsync($"{Route}/modules/{moduleId}");
        response.IsSuccessStatusCode.Should().BeTrue();
        return await response.ToResponseModel<FormModuleWithChildrenDto>();
    }

    private async Task<DcFormSectionDto> CreateSectionAsync(Guid moduleId, string title)
    {
        var response = await Client.PostAsJsonAsync(
            $"{Route}/modules/{moduleId}/sections",
            new CreateFormSectionRequestDto(title, null, null));
        response.IsSuccessStatusCode.Should().BeTrue();

        return (await GetModuleTreeAsync(moduleId)).Sections.Single(s => s.Title == title);
    }

    private async Task<DcFormQuestionDto> CreateQuestionAsync(
        Guid moduleId,
        string code,
        string questionType,
        IReadOnlyList<FormModuleQuestionOptionRequestDto>? options = null)
    {
        var response = await Client.PostAsJsonAsync(
            $"{Route}/modules/{moduleId}/questions",
            new CreateFormQuestionRequestDto(null, code, $"Question {code}", questionType, true, null, null, options));
        response.IsSuccessStatusCode.Should().BeTrue();

        return (await GetModuleTreeAsync(moduleId)).Questions.Single(q => q.Code == code);
    }

    private async Task<AuditLog> AuditRowAsync(Guid targetId, string action)
    {
        var rows = await Context.AuditLogs.AsNoTracking()
            .Where(a => a.TargetId == targetId && a.Action == action)
            .ToListAsync();
        var row = rows.Should().ContainSingle().Subject;
        row.TargetType.Should().Be(AuditTargetType.DataCollectionForm);

        return row;
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("error").GetString();
    }

    // jsonb re-spaces what was stored, so a snapshot is read as JSON rather than matched as text.
    private static JsonElement Property(string? snapshot, string name)
    {
        using var document = JsonDocument.Parse(snapshot!);
        return document.RootElement.GetProperty(name).Clone();
    }

    private static List<Guid> OrderedIds(string? snapshot) =>
        Property(snapshot, "orderedIds").EnumerateArray().Select(x => x.GetGuid()).ToList();

    [Fact]
    public async Task GetModules_AsNonAdmin_IsForbidden()
    {
        AuthenticateAs(Role.Lead, Guid.NewGuid(), Guid.NewGuid());

        var response = await Client.GetAsync($"{Route}/modules");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}