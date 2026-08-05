using Domain.Common;
using Domain.Users;

namespace Domain.Forms;

// A single question on the activity-record form. Code is the stable answer key and the reference used by
// conditional-reveal rules; it is unique across the whole form and immutable after creation so persisted
// answers always resolve. Options are owned by the question (choice types only).
public class FormQuestion : IEntity<FormQuestionId>
{
    private readonly List<FormQuestionOption> _options = new();

    public FormQuestionId Id { get; private set; }
    public FormSectionId SectionId { get; private set; }
    public string Code { get; private set; }
    public string Label { get; private set; }
    public string? Hint { get; private set; }
    public string? HelpText { get; private set; }
    public string? HelpTextSummary { get; private set; }
    public QuestionType QuestionType { get; private set; }
    public bool IsRequired { get; private set; }
    public bool IsActive { get; private set; }
    public int DisplayOrder { get; private set; }
    public string? ConditionalQuestionCode { get; private set; }
    public string? ConditionalValue { get; private set; }
    public UserId CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public IReadOnlyCollection<FormQuestionOption> Options => _options.AsReadOnly();

    private FormQuestion(
        FormQuestionId id,
        FormSectionId sectionId,
        string code,
        string label,
        string? hint,
        string? helpText,
        string? helpTextSummary,
        QuestionType questionType,
        bool isRequired,
        bool isActive,
        int displayOrder,
        string? conditionalQuestionCode,
        string? conditionalValue,
        UserId createdBy,
        DateTime createdAt,
        DateTime? updatedAt)
    {
        Id = id;
        SectionId = sectionId;
        Code = code;
        Label = label;
        Hint = hint;
        HelpText = helpText;
        HelpTextSummary = helpTextSummary;
        QuestionType = questionType;
        IsRequired = isRequired;
        IsActive = isActive;
        DisplayOrder = displayOrder;
        ConditionalQuestionCode = conditionalQuestionCode;
        ConditionalValue = conditionalValue;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public static FormQuestion Create(
        FormQuestionId id,
        FormSectionId sectionId,
        string code,
        string label,
        QuestionType questionType,
        int displayOrder,
        UserId createdBy,
        bool isRequired = false,
        string? hint = null,
        string? helpText = null,
        string? helpTextSummary = null,
        string? conditionalQuestionCode = null,
        string? conditionalValue = null)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNull(sectionId, nameof(sectionId));
        Guard.NotNullOrEmptyOrWhiteSpace(code, nameof(code));
        Guard.NotNullOrEmptyOrWhiteSpace(label, nameof(label));
        Guard.NotNull(questionType, nameof(questionType));
        Guard.NotNull(createdBy, nameof(createdBy));

        return new FormQuestion(
            id,
            sectionId,
            code.Trim(),
            label.Trim(),
            Normalise(hint),
            Normalise(helpText),
            Normalise(helpTextSummary),
            questionType,
            isRequired,
            isActive: true,
            displayOrder,
            Normalise(conditionalQuestionCode),
            Normalise(conditionalValue),
            createdBy,
            DateTime.UtcNow,
            null);
    }

    public void UpdateDetails(
        string label,
        QuestionType questionType,
        bool isRequired,
        string? hint = null,
        string? helpText = null,
        string? helpTextSummary = null,
        string? conditionalQuestionCode = null,
        string? conditionalValue = null)
    {
        Guard.NotNullOrEmptyOrWhiteSpace(label, nameof(label));
        Guard.NotNull(questionType, nameof(questionType));

        Label = label.Trim();
        QuestionType = questionType;
        IsRequired = isRequired;
        Hint = Normalise(hint);
        HelpText = Normalise(helpText);
        HelpTextSummary = Normalise(helpTextSummary);
        ConditionalQuestionCode = Normalise(conditionalQuestionCode);
        ConditionalValue = Normalise(conditionalValue);
        UpdatedAt = DateTime.UtcNow;
    }

    public void MoveToSection(FormSectionId sectionId)
    {
        Guard.NotNull(sectionId, nameof(sectionId));
        SectionId = sectionId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetDisplayOrder(int displayOrder)
    {
        DisplayOrder = displayOrder;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Activate()
    {
        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }

    public void AddOption(FormQuestionOptionId id, string value, string label, int displayOrder)
    {
        _options.Add(FormQuestionOption.Create(id, Id, value, label, displayOrder));
        UpdatedAt = DateTime.UtcNow;
    }

    // Used by the update flow, which receives the full option list and replaces the set wholesale.
    public void ClearOptions()
    {
        _options.Clear();
        UpdatedAt = DateTime.UtcNow;
    }

    private static string? Normalise(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}