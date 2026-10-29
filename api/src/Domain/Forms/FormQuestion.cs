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

    // Question Protocol copy (BLUM-7620). ErrorMessage is the protocol's "empty on submit" message, shown instead
    // of the generated "Enter an answer for ..." fallback. MaxLength drives both the GDS character count and the
    // over-length rule, whose message is MaxLengthErrorMessage because the protocol names the field differently
    // there ("Description of plans ...") than the question asks it ("Can you tell us about your approach ...").
    public string? ErrorMessage { get; private set; }

    public int? MaxLength { get; private set; }

    public string? MaxLengthErrorMessage { get; private set; }

    // What an amount is held to and how its messages name it (BLUM-8955). MinValue is the smallest amount a
    // Currency answer may be. The two subjects are the protocol's name for the field, which is not the question:
    // ErrorSubject starts a message ("Amount agreed must not include £"), ErrorSubjectInline reads mid-sentence
    // ("Enter an amount of 1 or more for the amount you have agreed"). Null means no minimum, and messages that
    // quote the label.
    public int? MinValue { get; private set; }

    public string? ErrorSubject { get; private set; }

    public string? ErrorSubjectInline { get; private set; }

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
        string? errorMessage,
        int? maxLength,
        string? maxLengthErrorMessage,
        int? minValue,
        string? errorSubject,
        string? errorSubjectInline,
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
        ErrorMessage = errorMessage;
        MaxLength = maxLength;
        MaxLengthErrorMessage = maxLengthErrorMessage;
        MinValue = minValue;
        ErrorSubject = errorSubject;
        ErrorSubjectInline = errorSubjectInline;
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
        DateTime utcNow,
        bool isRequired = false,
        string? hint = null,
        string? helpText = null,
        string? helpTextSummary = null,
        string? conditionalQuestionCode = null,
        string? conditionalValue = null,
        string? errorMessage = null,
        int? maxLength = null,
        string? maxLengthErrorMessage = null,
        int? minValue = null,
        string? errorSubject = null,
        string? errorSubjectInline = null)
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
            Normalise(errorMessage),
            maxLength,
            Normalise(maxLengthErrorMessage),
            minValue,
            Normalise(errorSubject),
            Normalise(errorSubjectInline),
            createdBy,
            utcNow,
            null);
    }

    public void UpdateDetails(
        string label,
        QuestionType questionType,
        bool isRequired,
        DateTime utcNow,
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
        UpdatedAt = utcNow;
    }

    // Deliberately NOT part of UpdateDetails: that is the Form Designer's surface, and its caller has no fields
    // for these, so folding them in would null the protocol's error copy and character limit every time a DHSC
    // admin edited a hint — with no seeder left to restore them (SeedFormDesigner only fills an empty schema).
    public void SetProtocolCopy(string? errorMessage, int? maxLength, string? maxLengthErrorMessage, DateTime utcNow)
    {
        ErrorMessage = Normalise(errorMessage);
        MaxLength = maxLength;
        MaxLengthErrorMessage = Normalise(maxLengthErrorMessage);
        UpdatedAt = utcNow;
    }

    // Kept off UpdateDetails for the same reason as SetProtocolCopy: the Form Designer has no fields for these.
    public void SetAmountRules(int? minValue, string? errorSubject, string? errorSubjectInline, DateTime utcNow)
    {
        MinValue = minValue;
        ErrorSubject = Normalise(errorSubject);
        ErrorSubjectInline = Normalise(errorSubjectInline);
        UpdatedAt = utcNow;
    }

    public void MoveToSection(FormSectionId sectionId, DateTime utcNow)
    {
        Guard.NotNull(sectionId, nameof(sectionId));
        SectionId = sectionId;
        UpdatedAt = utcNow;
    }

    public void SetDisplayOrder(int displayOrder, DateTime utcNow)
    {
        DisplayOrder = displayOrder;
        UpdatedAt = utcNow;
    }

    public void Activate(DateTime utcNow)
    {
        IsActive = true;
        UpdatedAt = utcNow;
    }

    public void Deactivate(DateTime utcNow)
    {
        IsActive = false;
        UpdatedAt = utcNow;
    }

    public void AddOption(FormQuestionOptionId id, string value, string label, int displayOrder, DateTime utcNow)
    {
        _options.Add(FormQuestionOption.Create(id, Id, value, label, displayOrder));
        UpdatedAt = utcNow;
    }

    // Used by the update flow, which receives the full option list and replaces the set wholesale.
    public void ClearOptions(DateTime utcNow)
    {
        _options.Clear();
        UpdatedAt = utcNow;
    }

    private static string? Normalise(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}