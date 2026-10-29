using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Domain.DataCollectionForms;

namespace Application.DataCollectionForms.Versions;

// BLUM-9102: the content of a FormModuleVersion. Json is what a version stores — the whole module, ids included, so
// a later read can tie answers back to question rows. Hash decides whether a publish changed anything, so it is taken
// over the same content WITHOUT ids and in a fixed order: UpdateFormQuestion and the strategic-feedback seeder re-mint
// option ids, and the database returns children in no particular order, and neither is a change to the form. A
// question names its section by number in the hash, so moving a question between sections still counts.
//
// Every field a question has goes in both (keep in step with DataCollectionFormSnapshot.Serialize(DcFormQuestion)).
// Form modules have no show/hide rules today, so there are none to carry. SchemaVersion is for the day the shape
// changes: a reader of an old version can tell which shape it holds. Parse reads the stored Json back into the same
// records, for the version view and for restoring a version into a draft.
public static class FormModuleDefinition
{
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static (string Json, string Hash) From(FormModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        var sections = module.Sections
            .OrderBy(s => s.SectionNumber)
            .ThenBy(s => s.Title, StringComparer.Ordinal)
            .ToList();
        var sectionNumbers = sections.ToDictionary(s => s.Id, s => s.SectionNumber);
        var questions = module.Questions
            .OrderBy(q => q.DisplayOrder)
            .ThenBy(q => q.Code, StringComparer.Ordinal)
            .ToList();

        var document = new FormModuleDefinitionDocument(
            SchemaVersion,
            module.Id.Value,
            module.Code,
            module.Name,
            module.Description,
            sections
                .Select(s => new FormSectionDefinition(s.Id.Value, s.SectionNumber, s.Title, s.Description, s.HelpText, s.IsActive))
                .ToList(),
            questions
                .Select(q => new FormQuestionDefinition(
                    q.Id.Value,
                    q.SectionId?.Value,
                    q.Code,
                    q.Label,
                    q.QuestionType.ToString(),
                    q.DisplayOrder,
                    q.IsRequired,
                    q.Hint,
                    q.HelpText,
                    q.ErrorMessage,
                    q.MaxLength,
                    q.MaxLengthErrorMessage,
                    q.IsActive,
                    OrderedOptions(q).Select(o => new FormOptionDefinition(o.Value, o.Label, o.DisplayOrder, o.IsActive)).ToList()))
                .ToList());

        var content = JsonSerializer.Serialize(new
        {
            schemaVersion = SchemaVersion,
            code = module.Code,
            name = module.Name,
            description = module.Description,
            sections = sections.Select(s => new
            {
                sectionNumber = s.SectionNumber,
                title = s.Title,
                description = s.Description,
                helpText = s.HelpText,
                isActive = s.IsActive,
            }),
            questions = questions.Select(q => new
            {
                sectionNumber = q.SectionId is { } sectionId && sectionNumbers.TryGetValue(sectionId, out var number)
                    ? number
                    : (int?)null,
                code = q.Code,
                label = q.Label,
                questionType = q.QuestionType.ToString(),
                displayOrder = q.DisplayOrder,
                isRequired = q.IsRequired,
                hint = q.Hint,
                helpText = q.HelpText,
                errorMessage = q.ErrorMessage,
                maxLength = q.MaxLength,
                maxLengthErrorMessage = q.MaxLengthErrorMessage,
                isActive = q.IsActive,
                options = OrderedOptions(q).Select(o => new
                {
                    value = o.Value,
                    label = o.Label,
                    displayOrder = o.DisplayOrder,
                    isActive = o.IsActive,
                }),
            }),
        });

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        return (JsonSerializer.Serialize(document, JsonOptions), hash);
    }

    // BLUM-9104: the form as a stored version holds it, as a detached FormModule that is never saved — so every reader
    // typed on the live entities (the report resolvers, validators, presenters and offline sheets) reads a version
    // unchanged. Question and section ids are the stored ones, so a question keeps the id a report has already seen;
    // options get fresh ids, which nothing reads. Status stays Draft: the entity only accepts children as a draft.
    public static FormModule ToModule(FormModuleVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        var definition = Parse(version.Definition);
        var actor = version.PublishedBy;
        var at = version.PublishedAt;
        var module = FormModule.Create(version.FormModuleId, definition.Code, definition.Name, 0, actor, at, definition.Description);

        foreach (var stored in definition.Sections)
        {
            var section = module.AddSection(DcFormSectionId.From(stored.Id), stored.SectionNumber, stored.Title, actor, at, stored.Description, stored.HelpText);
            if (!stored.IsActive)
            {
                section.Deactivate();
            }
        }

        foreach (var stored in definition.Questions)
        {
            var question = module.AddQuestion(
                DcFormQuestionId.From(stored.Id),
                stored.Code,
                stored.Label,
                Domain.Forms.QuestionType.From(stored.QuestionType),
                stored.DisplayOrder,
                actor,
                at,
                stored.SectionId is { } sectionId ? DcFormSectionId.From(sectionId) : null,
                stored.IsRequired,
                stored.Hint,
                stored.HelpText,
                stored.ErrorMessage,
                stored.MaxLength,
                stored.MaxLengthErrorMessage);
            foreach (var option in stored.Options)
            {
                question.AddOption(DcFormQuestionOptionId.New(), option.Value, option.Label, option.DisplayOrder, option.IsActive);
            }

            if (!stored.IsActive)
            {
                question.Deactivate();
            }
        }

        return module;
    }

    // Throws JsonException on a definition that is not this shape. Every stored version is schema 1 today.
    public static FormModuleDefinitionDocument Parse(string json) =>
        JsonSerializer.Deserialize<FormModuleDefinitionDocument>(json, JsonOptions)
        ?? throw new JsonException("Form module version definition is empty.");

    private static IEnumerable<DcFormQuestionOption> OrderedOptions(DcFormQuestion question) =>
        question.Options
            .OrderBy(o => o.DisplayOrder)
            .ThenBy(o => o.Value, StringComparer.Ordinal);
}

public sealed record FormModuleDefinitionDocument(
    int SchemaVersion,
    Guid ModuleId,
    string Code,
    string Name,
    string? Description,
    IReadOnlyList<FormSectionDefinition> Sections,
    IReadOnlyList<FormQuestionDefinition> Questions);

public sealed record FormSectionDefinition(Guid Id, int SectionNumber, string Title, string? Description, string? HelpText, bool IsActive);

public sealed record FormQuestionDefinition(
    Guid Id,
    Guid? SectionId,
    string Code,
    string Label,
    string QuestionType,
    int DisplayOrder,
    bool IsRequired,
    string? Hint,
    string? HelpText,
    string? ErrorMessage,
    int? MaxLength,
    string? MaxLengthErrorMessage,
    bool IsActive,
    IReadOnlyList<FormOptionDefinition> Options);

public sealed record FormOptionDefinition(string Value, string Label, int DisplayOrder, bool IsActive);