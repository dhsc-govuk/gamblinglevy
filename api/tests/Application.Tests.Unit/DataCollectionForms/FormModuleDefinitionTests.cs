using System.Text.Json;
using Application.DataCollectionForms.Versions;
using Domain.DataCollectionForms;
using Domain.Forms;
using Domain.Users;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.DataCollectionForms;

// BLUM-9102: the hash is what decides that a publish changed nothing, so it must ignore ids and database order and
// must notice every change to what an LA is asked.
public class FormModuleDefinitionTests
{
    private static readonly UserId Actor = UserId.New();
    private static readonly DateTime UtcNow = DateTime.UtcNow;

    [Fact]
    public void ShouldGiveTheSameHash_ForTheSameContentWithDifferentIdsAndOrder()
    {
        var first = BuildModule(reverseInsertionOrder: false);
        var second = BuildModule(reverseInsertionOrder: true);

        FormModuleDefinition.From(second).Hash.Should().Be(FormModuleDefinition.From(first).Hash);
        FormModuleDefinition.From(second).Json.Should().NotBe(FormModuleDefinition.From(first).Json, "the stored definition keeps ids");
    }

    [Fact]
    public void ShouldGiveTheSameHash_WhenOptionsAreRemintedWithNewIds()
    {
        var module = BuildModule();
        var before = FormModuleDefinition.From(module).Hash;

        var radio = Question(module, "Q_DELIVERED");
        radio.ClearOptions();
        radio.AddOption(DcFormQuestionOptionId.New(), "yes", "Yes", 0);
        radio.AddOption(DcFormQuestionOptionId.New(), "no", "No", 1);

        FormModuleDefinition.From(module).Hash.Should().Be(before);
    }

    [Theory]
    [InlineData("label")]
    [InlineData("required")]
    [InlineData("hint")]
    [InlineData("option-value")]
    [InlineData("option-label")]
    [InlineData("option-added")]
    [InlineData("question-added")]
    [InlineData("question-removed")]
    [InlineData("question-moved-section")]
    [InlineData("question-reordered")]
    [InlineData("question-deactivated")]
    [InlineData("section-title")]
    [InlineData("module-name")]
    [InlineData("max-length")]
    public void ShouldGiveADifferentHash_WhenWhatAnLaIsAskedChanges(string change)
    {
        var module = BuildModule();
        var before = FormModuleDefinition.From(module).Hash;
        var radio = Question(module, "Q_DELIVERED");
        var notes = Question(module, "Q_NOTES");

        switch (change)
        {
            case "label":
                radio.UpdateDetails("Did you deliver it?", radio.QuestionType, radio.IsRequired, radio.Hint);
                break;
            case "required":
                notes.UpdateDetails(notes.Label, notes.QuestionType, isRequired: true, notes.Hint);
                break;
            case "hint":
                notes.UpdateDetails(notes.Label, notes.QuestionType, notes.IsRequired, "Keep it short");
                break;
            case "option-value":
                radio.ClearOptions();
                radio.AddOption(DcFormQuestionOptionId.New(), "y", "Yes", 0);
                radio.AddOption(DcFormQuestionOptionId.New(), "no", "No", 1);
                break;
            case "option-label":
                radio.ClearOptions();
                radio.AddOption(DcFormQuestionOptionId.New(), "yes", "Yes, fully", 0);
                radio.AddOption(DcFormQuestionOptionId.New(), "no", "No", 1);
                break;
            case "option-added":
                radio.AddOption(DcFormQuestionOptionId.New(), "partly", "Partly", 2);
                break;
            case "question-added":
                module.AddQuestion(DcFormQuestionId.New(), "Q_EXTRA", "Anything else?", QuestionType.Text, 3, Actor, UtcNow);
                break;
            case "question-removed":
                module.RemoveQuestion(notes.Id, UtcNow);
                break;
            case "question-moved-section":
                notes.SetSection(module.Sections.Single().Id);
                break;
            case "question-reordered":
                notes.SetDisplayOrder(0);
                radio.SetDisplayOrder(1);
                break;
            case "question-deactivated":
                notes.Deactivate();
                break;
            case "section-title":
                module.Sections.Single().UpdateDetails("Delivery", null, null);
                break;
            case "module-name":
                module.UpdateDetails("Renamed module", null, Actor, UtcNow);
                break;
            case "max-length":
                notes.SetProtocolCopy(null, 500, null);
                break;
        }

        FormModuleDefinition.From(module).Hash.Should().NotBe(before);
    }

    [Fact]
    public void ShouldStoreEveryQuestionField_InTheDefinition()
    {
        var module = BuildModule();

        using var json = JsonDocument.Parse(FormModuleDefinition.From(module).Json);

        var radio = json.RootElement.GetProperty("questions").EnumerateArray().First();
        radio.GetProperty("id").GetGuid().Should().Be(Question(module, "Q_DELIVERED").Id.Value);
        radio.GetProperty("sectionId").GetGuid().Should().Be(module.Sections.Single().Id.Value);
        radio.GetProperty("questionType").GetString().Should().Be("Radio");
        radio.GetProperty("isRequired").GetBoolean().Should().BeTrue();
        radio.GetProperty("errorMessage").GetString().Should().Be("Select yes or no");
        radio.GetProperty("options").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void ShouldReadBackEveryField_FromTheStoredDefinition()
    {
        var module = BuildModule();
        var radio = Question(module, "Q_DELIVERED");

        var parsed = FormModuleDefinition.Parse(FormModuleDefinition.From(module).Json);

        parsed.SchemaVersion.Should().Be(FormModuleDefinition.SchemaVersion);
        parsed.Code.Should().Be("test_module");
        parsed.Sections.Should().ContainSingle().Which.Id.Should().Be(module.Sections.Single().Id.Value);
        parsed.Questions.Select(q => q.Code).Should().Equal("Q_DELIVERED", "Q_NOTES");
        var stored = parsed.Questions[0];
        stored.Id.Should().Be(radio.Id.Value);
        stored.SectionId.Should().Be(module.Sections.Single().Id.Value);
        stored.QuestionType.Should().Be("Radio");
        stored.IsRequired.Should().BeTrue();
        stored.ErrorMessage.Should().Be("Select yes or no");
        stored.Options.Select(o => (o.Value, o.Label)).Should().Equal(("yes", "Yes"), ("no", "No"));
        parsed.Questions[1].SectionId.Should().BeNull();
    }

    // BLUM-9104: a report reads a version through ToModule, so the module it builds must hold exactly what was saved.
    [Fact]
    public void ShouldRebuildTheSameForm_FromAStoredVersion()
    {
        var module = BuildModule();
        Question(module, "Q_NOTES").Deactivate();
        var (json, hash) = FormModuleDefinition.From(module);
        var version = FormModuleVersion.Create(FormModuleVersionId.New(), module.Id, 1, json, hash, Actor, UtcNow);

        var rebuilt = FormModuleDefinition.ToModule(version);

        FormModuleDefinition.From(rebuilt).Hash.Should().Be(hash);
        rebuilt.Id.Should().Be(module.Id);
        rebuilt.Questions.Select(q => q.Id).Should().BeEquivalentTo(module.Questions.Select(q => q.Id), "a report keeps seeing the same question ids");
        rebuilt.Sections.Single().Id.Should().Be(module.Sections.Single().Id);
        Question(rebuilt, "Q_NOTES").IsActive.Should().BeFalse();
    }

    // One section with a required radio question; an optional textarea outside any section.
    private static FormModule BuildModule(bool reverseInsertionOrder = false)
    {
        var module = FormModule.Create(FormModuleId.New(), "test_module", "Test module", 0, Actor, UtcNow);
        var section = module.AddSection(DcFormSectionId.New(), 1, "Your delivery", Actor, UtcNow);

        DcFormQuestion AddRadio()
        {
            var radio = module.AddQuestion(
                DcFormQuestionId.New(),
                "Q_DELIVERED",
                "Did you deliver the activity?",
                QuestionType.Radio,
                1,
                Actor,
                UtcNow,
                section.Id,
                isRequired: true,
                errorMessage: "Select yes or no");
            if (reverseInsertionOrder)
            {
                radio.AddOption(DcFormQuestionOptionId.New(), "no", "No", 1);
                radio.AddOption(DcFormQuestionOptionId.New(), "yes", "Yes", 0);
            }
            else
            {
                radio.AddOption(DcFormQuestionOptionId.New(), "yes", "Yes", 0);
                radio.AddOption(DcFormQuestionOptionId.New(), "no", "No", 1);
            }

            return radio;
        }

        void AddNotes() =>
            module.AddQuestion(DcFormQuestionId.New(), "Q_NOTES", "Any other notes?", QuestionType.Textarea, 2, Actor, UtcNow);

        if (reverseInsertionOrder)
        {
            AddNotes();
            AddRadio();
        }
        else
        {
            AddRadio();
            AddNotes();
        }

        return module;
    }

    private static DcFormQuestion Question(FormModule module, string code) => module.Questions.Single(q => q.Code == code);
}