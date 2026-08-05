using Domain.Common;

namespace Domain.Forms;

// A selectable option for a choice-type question (radio/checkbox/select). Value is stored against an answer;
// Label is what the LA user sees. Options are owned by their FormQuestion.
public class FormQuestionOption : IEntity<FormQuestionOptionId>
{
    public FormQuestionOptionId Id { get; private set; }
    public FormQuestionId QuestionId { get; private set; }
    public string Value { get; private set; }
    public string Label { get; private set; }
    public int DisplayOrder { get; private set; }

    private FormQuestionOption(
        FormQuestionOptionId id,
        FormQuestionId questionId,
        string value,
        string label,
        int displayOrder)
    {
        Id = id;
        QuestionId = questionId;
        Value = value;
        Label = label;
        DisplayOrder = displayOrder;
    }

    public static FormQuestionOption Create(
        FormQuestionOptionId id,
        FormQuestionId questionId,
        string value,
        string label,
        int displayOrder)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNull(questionId, nameof(questionId));
        Guard.NotNullOrEmptyOrWhiteSpace(value, nameof(value));
        Guard.NotNullOrEmptyOrWhiteSpace(label, nameof(label));

        return new FormQuestionOption(id, questionId, value.Trim(), label.Trim(), displayOrder);
    }
}