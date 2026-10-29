using Domain.Forms;

namespace Application.MaturityAssessments.Protocol;

// The maturity assessment's field vocabulary. Every value but Roles IS the renderer vocabulary
// (Domain.Forms.QuestionTypes), so the shared answer validator, ReportAnswerPresenter and the client's
// GovUKDynamicQuestionRenderer take a maturity question exactly as they take a quarterly one. Roles is the one
// addition: the "Funded roles" repeater (Role 1 / Role 2 / Add another role) whose answer is a JSON array, rendered
// by a bespoke client component and validated by MaturityRolesAnswer.
public static class MaturityFieldTypes
{
    public const string Radio = QuestionTypes.Radio;
    public const string Checkbox = QuestionTypes.Checkbox;
    public const string Select = QuestionTypes.Select;
    public const string Text = QuestionTypes.Text;
    public const string Textarea = QuestionTypes.Textarea;
    public const string Roles = "Roles";

    public static bool IsFreeText(string fieldType) => fieldType is Text or Textarea;

    public static bool IsChoice(string fieldType) => fieldType is Radio or Checkbox or Select;
}