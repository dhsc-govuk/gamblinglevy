using Domain.Common;

namespace Domain.DataCollectionForms;

// A selectable option for a choice-type forms-builder question (radio/checkbox/select). Value is stored against
// an answer; Label is what the user sees. Options are owned by their DcFormQuestion.
public class DcFormQuestionOption : IEntity<DcFormQuestionOptionId>
{
    public DcFormQuestionOptionId Id { get; private set; }
    public DcFormQuestionId QuestionId { get; private set; }
    public string Value { get; private set; }
    public string Label { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; }

    private DcFormQuestionOption(
        DcFormQuestionOptionId id,
        DcFormQuestionId questionId,
        string value,
        string label,
        int displayOrder,
        bool isActive)
    {
        Id = id;
        QuestionId = questionId;
        Value = value;
        Label = label;
        DisplayOrder = displayOrder;
        IsActive = isActive;
    }

    internal static DcFormQuestionOption Create(
        DcFormQuestionOptionId id,
        DcFormQuestionId questionId,
        string value,
        string label,
        int displayOrder,
        bool isActive = true)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNull(questionId, nameof(questionId));
        Guard.NotNullOrEmptyOrWhiteSpace(value, nameof(value));
        Guard.NotNullOrEmptyOrWhiteSpace(label, nameof(label));

        return new DcFormQuestionOption(id, questionId, value.Trim(), label.Trim(), displayOrder, isActive);
    }

    internal void UpdateDetails(string value, string label, int displayOrder, bool isActive)
    {
        Guard.NotNullOrEmptyOrWhiteSpace(value, nameof(value));
        Guard.NotNullOrEmptyOrWhiteSpace(label, nameof(label));

        Value = value.Trim();
        Label = label.Trim();
        DisplayOrder = displayOrder;
        IsActive = isActive;
    }
}