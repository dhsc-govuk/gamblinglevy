using Domain.Common;

namespace Domain.DataCollectionForms;

// A single question on a forms-builder module. Code is the stable answer key, unique within the module and
// immutable after creation so persisted answers always resolve. A question may sit inside a section or directly
// on the module (SectionId null). Options are owned by the question (choice types only). QuestionType is reused
// from Domain.Forms.
public class DcFormQuestion : IEntity<DcFormQuestionId>
{
    private readonly List<DcFormQuestionOption> _options = new();

    public DcFormQuestionId Id { get; private set; }
    public FormModuleId FormModuleId { get; private set; }
    public DcFormSectionId? SectionId { get; private set; }
    public string Code { get; private set; }
    public string Label { get; private set; }
    public Domain.Forms.QuestionType QuestionType { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsRequired { get; private set; }
    public string? Hint { get; private set; }
    public string? HelpText { get; private set; }
    public bool IsActive { get; private set; }

    public IReadOnlyCollection<DcFormQuestionOption> Options => _options.AsReadOnly();

    private DcFormQuestion(
        DcFormQuestionId id,
        FormModuleId formModuleId,
        DcFormSectionId? sectionId,
        string code,
        string label,
        Domain.Forms.QuestionType questionType,
        int displayOrder,
        bool isRequired,
        string? hint,
        string? helpText,
        bool isActive)
    {
        Id = id;
        FormModuleId = formModuleId;
        SectionId = sectionId;
        Code = code;
        Label = label;
        QuestionType = questionType;
        DisplayOrder = displayOrder;
        IsRequired = isRequired;
        Hint = hint;
        HelpText = helpText;
        IsActive = isActive;
    }

    internal static DcFormQuestion Create(
        DcFormQuestionId id,
        FormModuleId formModuleId,
        string code,
        string label,
        Domain.Forms.QuestionType questionType,
        int displayOrder,
        DcFormSectionId? sectionId = null,
        bool isRequired = false,
        string? hint = null,
        string? helpText = null)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNull(formModuleId, nameof(formModuleId));
        Guard.NotNullOrEmptyOrWhiteSpace(code, nameof(code));
        Guard.NotNullOrEmptyOrWhiteSpace(label, nameof(label));
        Guard.NotNull(questionType, nameof(questionType));

        return new DcFormQuestion(
            id,
            formModuleId,
            sectionId,
            code.Trim(),
            label.Trim(),
            questionType,
            displayOrder,
            isRequired,
            Normalise(hint),
            Normalise(helpText),
            isActive: true);
    }

    public void UpdateDetails(
        string label,
        Domain.Forms.QuestionType questionType,
        bool isRequired,
        string? hint = null,
        string? helpText = null)
    {
        Guard.NotNullOrEmptyOrWhiteSpace(label, nameof(label));
        Guard.NotNull(questionType, nameof(questionType));

        Label = label.Trim();
        QuestionType = questionType;
        IsRequired = isRequired;
        Hint = Normalise(hint);
        HelpText = Normalise(helpText);
    }

    public void SetSection(DcFormSectionId? sectionId)
    {
        SectionId = sectionId;
    }

    public void SetDisplayOrder(int displayOrder)
    {
        DisplayOrder = displayOrder;
    }

    public void AddOption(DcFormQuestionOptionId id, string value, string label, int displayOrder, bool isActive = true)
    {
        _options.Add(DcFormQuestionOption.Create(id, Id, value, label, displayOrder, isActive));
    }

    // Used by the update flow, which receives the full option list and replaces the set wholesale.
    public void ClearOptions()
    {
        _options.Clear();
    }

    public void Activate()
    {
        IsActive = true;
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    private static string? Normalise(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}