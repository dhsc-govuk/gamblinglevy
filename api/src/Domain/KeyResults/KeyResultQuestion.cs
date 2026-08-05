using Domain.Categories;
using Domain.Common;
using Domain.Users;

namespace Domain.KeyResults;

// One quarterly-report question from the Question Protocol's Results table (BLUM-6611): asked for an activity of a
// given type when the matching KeyResultCode was selected on the activity and the activity's status falls inside
// StatusScope. Code is the stable answer key — the Active and Closed/Finished phrasing variants are separate rows
// sharing one Code, so saved SubmissionActivityAnswer rows survive a status change. ConditionalQuestionCode/Value
// express in-form reveals (e.g. a follow-up shown when the parent boolean is answered "yes"), mirroring
// Domain.Forms.FormQuestion so the existing renderer/validator semantics apply unchanged. System-owned seed data.
public class KeyResultQuestion : IEntity<KeyResultQuestionId>
{
    public KeyResultQuestionId Id { get; private set; }
    public ActivityTaskId ActivityTaskId { get; private set; }
    public string KeyResultCode { get; private set; }
    public string Code { get; private set; }
    public QuestionStatusScope StatusScope { get; private set; }
    public string Label { get; private set; }
    public string? Hint { get; private set; }
    public ProtocolFieldType FieldType { get; private set; }
    public bool IsRequiredOnComplete { get; private set; }
    public decimal? MinValue { get; private set; }
    public decimal? MaxValue { get; private set; }
    public IReadOnlyList<KeyResultQuestionOption>? Options { get; private set; }
    public string? PrefillSource { get; private set; }
    public string? ConditionalQuestionCode { get; private set; }
    public string? ConditionalValue { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; }
    public UserId CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private KeyResultQuestion(
        KeyResultQuestionId id,
        ActivityTaskId activityTaskId,
        string keyResultCode,
        string code,
        QuestionStatusScope statusScope,
        string label,
        string? hint,
        ProtocolFieldType fieldType,
        bool isRequiredOnComplete,
        decimal? minValue,
        decimal? maxValue,
        IReadOnlyList<KeyResultQuestionOption>? options,
        string? prefillSource,
        string? conditionalQuestionCode,
        string? conditionalValue,
        string? errorMessage,
        int displayOrder,
        bool isActive,
        UserId createdBy,
        DateTime createdAt)
    {
        Id = id;
        ActivityTaskId = activityTaskId;
        KeyResultCode = keyResultCode;
        Code = code;
        StatusScope = statusScope;
        Label = label;
        Hint = hint;
        FieldType = fieldType;
        IsRequiredOnComplete = isRequiredOnComplete;
        MinValue = minValue;
        MaxValue = maxValue;
        Options = options;
        PrefillSource = prefillSource;
        ConditionalQuestionCode = conditionalQuestionCode;
        ConditionalValue = conditionalValue;
        ErrorMessage = errorMessage;
        DisplayOrder = displayOrder;
        IsActive = isActive;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
    }

    public static KeyResultQuestion Seed(
        KeyResultQuestionId id,
        ActivityTaskId activityTaskId,
        string keyResultCode,
        string code,
        QuestionStatusScope statusScope,
        string label,
        ProtocolFieldType fieldType,
        int displayOrder,
        UserId createdBy,
        string? hint = null,
        bool isRequiredOnComplete = true,
        decimal? minValue = null,
        decimal? maxValue = null,
        IReadOnlyList<KeyResultQuestionOption>? options = null,
        string? prefillSource = null,
        string? conditionalQuestionCode = null,
        string? conditionalValue = null,
        string? errorMessage = null)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNull(activityTaskId, nameof(activityTaskId));
        Guard.NotNullOrEmptyOrWhiteSpace(keyResultCode, nameof(keyResultCode));
        Guard.NotNullOrEmptyOrWhiteSpace(code, nameof(code));
        Guard.NotNull(statusScope, nameof(statusScope));
        Guard.NotNullOrEmptyOrWhiteSpace(label, nameof(label));
        Guard.NotNull(fieldType, nameof(fieldType));
        Guard.NotNull(createdBy, nameof(createdBy));

        return new KeyResultQuestion(
            id,
            activityTaskId,
            keyResultCode.Trim(),
            code.Trim(),
            statusScope,
            label.Trim(),
            Normalise(hint),
            fieldType,
            isRequiredOnComplete,
            minValue,
            maxValue,
            options,
            Normalise(prefillSource),
            Normalise(conditionalQuestionCode),
            Normalise(conditionalValue),
            Normalise(errorMessage),
            displayOrder,
            isActive: true,
            createdBy,
            DateTime.UtcNow);
    }

    private static string? Normalise(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}