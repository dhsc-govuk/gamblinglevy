using Application.Forms.Answers;
using Domain.DataCollectionForms;

namespace Application.Submissions;

// Validates "Provide strategic feedback" answers against the seeded module questions. The feedback questions are
// a small fixed set with no conditional logic, so this stays simpler than the protocol's SubmissionAnswerValidator:
// required-when-blank (when enforced), choice membership and a max-length guard. Reuses AnswerValidationError so the
// errors map onto the shared SUBMISSION_ANSWERS_INVALID envelope.
public static class StrategicFeedbackAnswerValidator
{
    private const int MaxTextLength = 10000;

    public static IReadOnlyList<AnswerValidationError> Validate(
        IReadOnlyList<DcFormQuestion> questions,
        IReadOnlyDictionary<string, string?> answers,
        bool enforceRequired)
    {
        var errors = new List<AnswerValidationError>();

        foreach (var question in questions)
        {
            var value = answers.TryGetValue(question.Code, out var raw) ? raw : null;

            if (string.IsNullOrWhiteSpace(value))
            {
                if (enforceRequired && question.IsRequired)
                {
                    errors.Add(new AnswerValidationError(question.Code, $"Enter an answer for “{question.Label}”."));
                }

                continue;
            }

            if (question.QuestionType.IsChoice && question.Options.All(option => !option.IsActive || option.Value != value.Trim()))
            {
                errors.Add(new AnswerValidationError(question.Code, $"Select an answer for “{question.Label}”."));
                continue;
            }

            if (value!.Trim().Length > MaxTextLength)
            {
                errors.Add(new AnswerValidationError(question.Code, $"“{question.Label}” is too long."));
            }
        }

        return errors;
    }

    // The answers that should actually be persisted: questions that have a non-empty value.
    public static IReadOnlyList<(DcFormQuestion Question, string Value)> CollectPersistable(
        IReadOnlyList<DcFormQuestion> questions,
        IReadOnlyDictionary<string, string?> answers)
    {
        var result = new List<(DcFormQuestion Question, string Value)>();

        foreach (var question in questions)
        {
            if (answers.TryGetValue(question.Code, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                result.Add((question, value.Trim()));
            }
        }

        return result;
    }
}