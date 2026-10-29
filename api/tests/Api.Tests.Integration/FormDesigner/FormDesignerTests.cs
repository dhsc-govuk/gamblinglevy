using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Dtos;
using Domain.AuditLogs;
using Domain.Categories;
using Domain.Forms;
using Domain.Users;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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

    // BLUM-8747: the activity form designer audits each create against the section or question, as a Form target.
    [Fact]
    public async Task ShouldWriteOneCreatedAuditRow_ForASectionAndForAQuestion()
    {
        var section = await CreateSectionAsync("Audited section");
        var question = await CreateQuestionAsync(TextQuestion(section.Id, "AUDITED_QUESTION"));

        var sectionRows = await Context.AuditLogs.AsNoTracking().Where(a => a.TargetId == section.Id).ToListAsync();
        var sectionRow = sectionRows.Should().ContainSingle().Subject;
        sectionRow.Action.Should().Be(AuditAction.FormSectionCreated);
        sectionRow.TargetType.Should().Be(AuditTargetType.Form);

        var questionRows = await Context.AuditLogs.AsNoTracking().Where(a => a.TargetId == question.Id).ToListAsync();
        var questionRow = questionRows.Should().ContainSingle().Subject;
        questionRow.Action.Should().Be(AuditAction.FormQuestionCreated);
        questionRow.TargetType.Should().Be(AuditTargetType.Form);
        questionRow.After.Should().Contain("AUDITED_QUESTION");
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

    // BLUM-7620: the protocol's error message and character limit are seed-owned columns on the same row the
    // Form Designer edits, and the update request has no fields for them. They deliberately live outside
    // UpdateDetails so an admin editing a label cannot null them — and nothing would restore them if it did,
    // because SeedFormDesigner only fills an empty schema.
    [Fact]
    public async Task ShouldPreserveSeededErrorCopyAndCharacterLimit_WhenAQuestionIsEdited()
    {
        var section = await CreateSectionAsync();
        var question = await CreateQuestionAsync(TextQuestion(section.Id, "PROTOCOL_COPY"));

        var seeded = await Context.FormQuestions.FirstAsync(q => q.Id == new FormQuestionId(question.Id));
        seeded.SetProtocolCopy("Enter a description of what you plan to do", 500, "Description of plans must be 500 characters or less", DateTime.UtcNow);

        // BLUM-8955 added three more of the same kind: an amount's minimum and the protocol's name for the field.
        seeded.SetAmountRules(1, "Amount agreed", "the amount you have agreed", DateTime.UtcNow);
        await SaveChangesAsync();

        var response = await Client.PutAsJsonAsync(
            $"admin/activity-form/questions/{question.Id}",
            new UpdateQuestionRequestDto(section.Id, "Reworded label", "Text", true, true, "A new hint", null, null, null, null, null));
        response.EnsureSuccessStatusCode();

        var updated = (await GetSchemaAsync()).Questions.Single(q => q.Code == "PROTOCOL_COPY");
        updated.Label.Should().Be("Reworded label", "the edit itself must still apply");
        updated.Hint.Should().Be("A new hint");
        updated.ErrorMessage.Should().Be("Enter a description of what you plan to do");
        updated.MaxLength.Should().Be(500);
        updated.MaxLengthErrorMessage.Should().Be("Description of plans must be 500 characters or less");
        updated.MinValue.Should().Be(1);
        updated.ErrorSubject.Should().Be("Amount agreed");
        updated.ErrorSubjectInline.Should().Be("the amount you have agreed");
    }

    [Fact]
    public async Task ShouldReorderSections_FromZero_AndAuditTheNewOrder()
    {
        var a = await CreateSectionAsync("Section A");
        var b = await CreateSectionAsync("Section B");
        var c = await CreateSectionAsync("Section C");
        await ClearAuditLogAsync();

        var response = await Client.PostAsJsonAsync(
            "admin/activity-form/sections/reorder",
            new ReorderRequestDto(new List<Guid> { c.Id, a.Id, b.Id }));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Creating a section numbers it from 1; a reorder renumbers the whole list from 0.
        var sections = (await GetSchemaAsync()).Sections.OrderBy(s => s.DisplayOrder).ToList();
        sections.Select(s => s.Id).Should().Equal(c.Id, a.Id, b.Id);
        sections.Select(s => s.DisplayOrder).Should().Equal(0, 1, 2);

        // The sections have no parent row to hang the audit on, so the target is Guid.Empty and the row is
        // found by its action — which is why the log is emptied first.
        var row = (await Context.AuditLogs.AsNoTracking().ToListAsync()).Should().ContainSingle().Subject;
        row.Action.Should().Be(AuditAction.FormSectionsReordered);
        row.TargetType.Should().Be(AuditTargetType.Form);
        row.TargetId.Should().Be(Guid.Empty);
        OrderedIds(row.Before).Should().Equal(a.Id, b.Id, c.Id);
        OrderedIds(row.After).Should().Equal(c.Id, a.Id, b.Id);
    }

    // Pinned as it is, not as it should be: an id that names no section is skipped rather than refused, and
    // the sections that were named take the positions they were given, gap included. The data-collection-form
    // builder answers 404 for the same request (see DataCollectionFormsControllerTests).
    [Fact]
    public async Task ShouldIgnoreUnknownIds_WhenReorderingSections()
    {
        var a = await CreateSectionAsync("Section A");
        var b = await CreateSectionAsync("Section B");

        var response = await Client.PostAsJsonAsync(
            "admin/activity-form/sections/reorder",
            new ReorderRequestDto(new List<Guid> { Guid.NewGuid(), b.Id, a.Id }));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var sections = (await GetSchemaAsync()).Sections;
        sections.Single(s => s.Id == b.Id).DisplayOrder.Should().Be(1);
        sections.Single(s => s.Id == a.Id).DisplayOrder.Should().Be(2);
    }

    [Fact]
    public async Task ShouldListActiveCategories_InSequenceOrder()
    {
        // Arrange — the categories are seeded reference data and are left alone. Two more are added below
        // every seeded sequence number: one active, which must therefore come first although it was inserted
        // last, and one retired, which must not come at all.
        var retired = Category.Seed(CategoryId.New(), "Retired category", -2, UserId.New(), DateTime.UtcNow, isActive: false);
        var earliest = Category.Seed(CategoryId.New(), "Earliest category", -1, UserId.New(), DateTime.UtcNow);
        await Context.Categories.AddRangeAsync(retired, earliest);
        await SaveChangesAsync();

        try
        {
            // Act
            var response = await Client.GetAsync("admin/activity-form/categories");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var categories = await response.ToResponseModel<List<TaxonomyCategoryDto>>();

            var expected = await Context.Categories.AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.SequenceOrder)
                .Select(x => x.Id)
                .ToListAsync();

            categories.Select(x => x.Id).Should().Equal(expected.Select(x => x.Value));
            categories.Should().HaveCountGreaterThan(1);
            categories[0].Should().Be(new TaxonomyCategoryDto(earliest.Id.Value, "Earliest category"));
            categories.Should().NotContain(x => x.Id == retired.Id.Value);
        }
        finally
        {
            Context.Categories.RemoveRange(Context.Categories.Where(x => x.Id == retired.Id || x.Id == earliest.Id));
            await SaveChangesAsync();
        }
    }

    [Fact]
    public async Task ShouldUpdateSection_AndAuditIt()
    {
        var section = await CreateSectionAsync("Original title", "Original description");
        var category = await Context.Categories.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.SequenceOrder).FirstAsync();

        var response = await Client.PutAsJsonAsync(
            $"admin/activity-form/sections/{section.Id}",
            new UpdateSectionRequestDto("New title", "New description", category.Id.Value));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.ToResponseModel<FormSectionDto>();
        updated.Should().Be(section with { Title = "New title", Description = "New description", CategoryId = category.Id.Value });

        var saved = await Context.FormSections.AsNoTracking().FirstAsync(x => x.Id == new FormSectionId(section.Id));
        saved.Title.Should().Be("New title");
        saved.Description.Should().Be("New description");
        saved.CategoryId.Should().Be(category.Id);

        var rows = await Context.AuditLogs.AsNoTracking().Where(x => x.TargetId == section.Id).ToListAsync();
        var row = rows.Should().ContainSingle(x => x.Action == AuditAction.FormSectionUpdated).Subject;
        row.TargetType.Should().Be(AuditTargetType.Form);

        using var before = JsonDocument.Parse(row.Before!);
        using var after = JsonDocument.Parse(row.After!);
        before.RootElement.GetProperty("title").GetString().Should().Be("Original title");
        before.RootElement.GetProperty("categoryId").ValueKind.Should().Be(JsonValueKind.Null);
        after.RootElement.GetProperty("title").GetString().Should().Be("New title");
        after.RootElement.GetProperty("categoryId").GetGuid().Should().Be(category.Id.Value);
    }

    [Fact]
    public async Task ShouldReturn404_WhenUpdatingAnUnknownSection()
    {
        var response = await Client.PutAsJsonAsync(
            $"admin/activity-form/sections/{Guid.NewGuid()}",
            new UpdateSectionRequestDto("Title", null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ShouldDeleteAnEmptySection_AndAuditIt()
    {
        var section = await CreateSectionAsync("Short-lived section");

        var response = await Client.DeleteAsync($"admin/activity-form/sections/{section.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetSchemaAsync()).Sections.Should().NotContain(s => s.Id == section.Id);
        (await Context.FormSections.AsNoTracking().AnyAsync(x => x.Id == new FormSectionId(section.Id))).Should().BeFalse();

        var rows = await Context.AuditLogs.AsNoTracking().Where(x => x.TargetId == section.Id).ToListAsync();
        var row = rows.Should().ContainSingle(x => x.Action == AuditAction.FormSectionDeleted).Subject;
        row.After.Should().BeNull();

        using var before = JsonDocument.Parse(row.Before!);
        before.RootElement.GetProperty("title").GetString().Should().Be("Short-lived section");
    }

    [Fact]
    public async Task ShouldDeleteQuestion_WithItsOptions_AndAuditIt()
    {
        var section = await CreateSectionAsync();
        var doomed = await CreateQuestionAsync(RadioQuestion(section.Id, "DOOMED"));
        var survivor = await CreateQuestionAsync(TextQuestion(section.Id, "SURVIVOR"));

        var response = await Client.DeleteAsync($"admin/activity-form/questions/{doomed.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The options go with their question; the other question in the section is untouched.
        (await GetSchemaAsync()).Questions.Select(q => q.Id).Should().Equal(survivor.Id);
        (await Context.FormQuestionOptions.AsNoTracking().AnyAsync(x => x.QuestionId == new FormQuestionId(doomed.Id))).Should().BeFalse();

        var rows = await Context.AuditLogs.AsNoTracking().Where(x => x.TargetId == doomed.Id).ToListAsync();
        var row = rows.Should().ContainSingle(x => x.Action == AuditAction.FormQuestionDeleted).Subject;
        row.After.Should().BeNull();

        using var before = JsonDocument.Parse(row.Before!);
        before.RootElement.GetProperty("code").GetString().Should().Be("DOOMED");
        before.RootElement.GetProperty("options").GetArrayLength().Should().Be(2);
    }

    private static List<Guid> OrderedIds(string? snapshot)
    {
        using var document = JsonDocument.Parse(snapshot!);
        return document.RootElement.GetProperty("orderedIds").EnumerateArray().Select(x => x.GetGuid()).ToList();
    }

    private async Task ClearAuditLogAsync()
    {
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        await SaveChangesAsync();
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

    // BLUM-9395: off, the designer's endpoints answer a bodiless 404 to a DHSC admin and write nothing.
    [Fact]
    public async Task ShouldAnswerNotFound_WhenTheFlagIsOff()
    {
        await SetFlagAsync(false);

        var schema = await Client.GetAsync("admin/activity-form/schema");
        schema.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await schema.Content.ReadAsStringAsync()).Should().BeEmpty();

        var create = await Client.PostAsJsonAsync(
            "admin/activity-form/sections",
            new CreateSectionRequestDto("Hidden", null));
        create.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await create.Content.ReadAsStringAsync()).Should().BeEmpty();

        (await Context.FormSections.AsNoTracking().AnyAsync()).Should().BeFalse();
    }
}