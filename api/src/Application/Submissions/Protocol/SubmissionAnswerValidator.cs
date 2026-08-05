using Application.Forms.Answers;
using Domain.Forms;

namespace Application.Submissions.Protocol;

// Validates quarterly-report answers against an activity's resolved question set. A sibling of
// ActivityAnswerValidator (which is bound to the activity-record FormQuestion schema) sharing its visibility and
// option semantics, plus the protocol's numeric rules: whole numbers only, Min/Max ranges, and the percentage
// phrasing from the Question Protocol's error columns. Required answers are only enforced when completing
// ("Save and come back later" passes enforceRequired: false); type/range errors always block so garbage never
// persists. Mirrors the frontend renderer/validators so the two never disagree.
public static class SubmissionAnswerValidator
{
    private const int MaxTextLength = 10000;

    public static bool IsVisible(ResolvedQuestion question, IReadOnlyDictionary<string, string?> answers)
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
        IReadOnlyList<ResolvedQuestion> questions,
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
                    errors.Add(new AnswerValidationError(
                        question.Code,
                        question.ErrorMessage ?? $"Enter an answer for “{question.Label}”."));
                }

                continue;
            }

            var trimmed = value!.Trim();
            switch (question.QuestionType)
            {
                case QuestionTypes.Number:
                    ValidateNumber(question, trimmed, errors);
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
    public static IReadOnlyList<(ResolvedQuestion Question, string Value)> CollectPersistable(
        IReadOnlyList<ResolvedQuestion> questions,
        IReadOnlyDictionary<string, string?> answers)
    {
        var result = new List<(ResolvedQuestion Question, string Value)>();

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

    private static void ValidateNumber(ResolvedQuestion question, string value, List<AnswerValidationError> errors)
    {
        // The protocol's numeric questions are all whole numbers (counts and percentages).
        if (!long.TryParse(value, out var number))
        {
            errors.Add(new AnswerValidationError(
                question.Code,
                question.IsPercentage ? "Enter a percentage, like 25" : $"“{question.Label}” must be a whole number."));
            return;
        }

        if ((question.MinValue.HasValue && number < question.MinValue.Value)
            || (question.MaxValue.HasValue && number > question.MaxValue.Value))
        {
            errors.Add(new AnswerValidationError(
                question.Code,
                question.IsPercentage
                    ? "Enter a percentage between 0 and 100"
                    : $"Enter a number of {question.MinValue} or more"));
        }
    }

    private static string[] Split(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}