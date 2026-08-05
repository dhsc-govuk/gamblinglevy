using Domain.Forms;

namespace Application.Forms.Answers;

public record AnswerValidationError(string QuestionCode, string Message);

// Validates dynamic-question answers submitted with an activity against the active form schema. Lives in
// Application (not FluentValidation) because it depends on the DB-loaded schema. Visibility, required and type
// rules mirror the frontend GovUKDynamicQuestionRenderer so the two never disagree.
public static class ActivityAnswerValidator
{
    private const int MaxTextLength = 10000;

    // A question is visible unless it has a conditional rule whose parent answer does not match. Matches the
    // renderer: ConditionalValue is a comma-separated allow-list; a checkbox parent may hold several values.
    public static bool IsVisible(FormQuestion question, IReadOnlyDictionary<string, string?> answers)
    {
        if (string.IsNullOrWhiteSpace(question.ConditionalQuestionCode) || string.IsNullOrWhiteSpace(question.ConditionalValue))
        {
            return true;
        }

        var parentAnswer = answers.TryGetValue(question.ConditionalQuestionCode, out var value) ? value : null;
        if (string.IsNullOrWhiteSpace(parentAnswer))
        {
            return false;
        }

        var allowed = Split(question.ConditionalValue);
        var parentValues = Split(parentAnswer);
        return parentValues.Any(allowed.Contains);
    }

    public static IReadOnlyList<AnswerValidationError> Validate(
        IReadOnlyList<FormQuestion> questions,
        IReadOnlyDictionary<string, string?> answers,
        bool enforceRequired)
    {
        var errors = new List<AnswerValidationError>();

        foreach (var question in questions)
        {
            if (!IsVisible(question, answers))
            {
                continue;
            }

            var value = answers.TryGetValue(question.Code, out var raw) ? raw : null;
            var isEmpty = string.IsNullOrWhiteSpace(value);

            if (isEmpty)
            {
                if (enforceRequired && question.IsRequired)
                {
                    errors.Add(new AnswerValidationError(question.Code, $"Enter an answer for “{question.Label}”."));
                }

                continue;
            }

            var trimmed = value!.Trim();
            switch (question.QuestionType.ToString())
            {
                case QuestionTypes.Number:
                    if (!decimal.TryParse(trimmed, out _))
                    {
                        errors.Add(new AnswerValidationError(question.Code, $"“{question.Label}” must be a number."));
                    }

                    break;

                case QuestionTypes.Radio:
                case QuestionTypes.Select:
                    if (question.Options.All(o => o.Value != trimmed))
                    {
                        errors.Add(new AnswerValidationError(question.Code, $"Select a valid option for “{question.Label}”."));
                    }

                    break;

                case QuestionTypes.Checkbox:
                    var selected = Split(trimmed);
                    if (selected.Any(s => question.Options.All(o => o.Value != s)))
                    {
                        errors.Add(new AnswerValidationError(question.Code, $"Select valid options for “{question.Label}”."));
                    }

                    break;

                default:
                    if (trimmed.Length > MaxTextLength)
                    {
                        errors.Add(new AnswerValidationError(question.Code, $"“{question.Label}” is too long."));
                    }

                    break;
            }
        }

        return errors;
    }

    // The answers that should actually be persisted: visible questions that have a non-empty value.
    public static IReadOnlyList<(FormQuestion Question, string Value)> CollectPersistable(
        IReadOnlyList<FormQuestion> questions,
        IReadOnlyDictionary<string, string?> answers)
    {
        var result = new List<(FormQuestion Question, string Value)>();

        foreach (var question in questions)
        {
            if (!IsVisible(question, answers))
            {
                continue;
            }

            if (!answers.TryGetValue(question.Code, out var value) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            result.Add((question, value.Trim()));
        }

        return result;
    }

    private static string[] Split(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}