using Application.Common.Validation;
using Domain.Forms;

namespace Application.Forms.Answers;

public record AnswerValidationError(string QuestionCode, string Message);

// Validates dynamic-question answers submitted with an activity against the active form schema. Lives in
// Application (not FluentValidation) because it depends on the DB-loaded schema. Visibility, required and type
// rules mirror the frontend GovUKDynamicQuestionRenderer so the two never disagree.
public static class ActivityAnswerValidator
{
    private const int MaxTextLength = FieldLimits.AnswerText;

    // Which of these questions are visible, judged among themselves (AnswerVisibility). transitive is BLUM_9087: on, a
    // question whose parent is hidden is hidden too.
    public static AnswerVisibility Visibility(
        IEnumerable<FormQuestion> questions,
        IReadOnlyDictionary<string, string?> answers,
        bool transitive) =>
        AnswerVisibility.Of(
            questions.Select(question => VisibilityNode.For(question.Code, question.ConditionalQuestionCode, question.ConditionalValue)),
            answers,
            transitive);

    public static IReadOnlyList<AnswerValidationError> Validate(
        IReadOnlyList<FormQuestion> questions,
        IReadOnlyDictionary<string, string?> answers,
        bool enforceRequired,
        bool transitive) =>
        Validate(questions, answers, enforceRequired, Visibility(questions, answers, transitive));

    // For a caller that validates part of a schema but judges visibility over all of it (the activity commands split
    // the schema by activity type, and a rule may point across that split). The visibility must have been built from the
    // same answers.
    internal static IReadOnlyList<AnswerValidationError> Validate(
        IReadOnlyList<FormQuestion> questions,
        IReadOnlyDictionary<string, string?> answers,
        bool enforceRequired,
        AnswerVisibility visibility)
    {
        var errors = new List<AnswerValidationError>();

        foreach (var question in questions)
        {
            if (!visibility.IsVisible(question.Code))
            {
                continue;
            }

            var value = answers.TryGetValue(question.Code, out var raw) ? raw : null;
            var isEmpty = string.IsNullOrWhiteSpace(value);

            if (isEmpty)
            {
                if (enforceRequired && question.IsRequired)
                {
                    errors.Add(new AnswerValidationError(question.Code, question.ErrorMessage ?? $"Enter an answer for “{question.Label}”."));
                }

                continue;
            }

            var trimmed = value!.Trim();

            // The protocol's own character limit, checked ahead of the type rules so an over-long answer is
            // reported as such whatever the field type. MaxTextLength below stays as the backstop for questions
            // the protocol gives no limit.
            if (question.MaxLength is { } maxLength && trimmed.Length > maxLength)
            {
                errors.Add(new AnswerValidationError(
                    question.Code,
                    question.MaxLengthErrorMessage ?? $"“{question.Label}” must be {maxLength} characters or less"));
                continue;
            }

            switch (question.QuestionType.ToString())
            {
                case QuestionTypes.Number:
                    if (!decimal.TryParse(trimmed, out _))
                    {
                        errors.Add(new AnswerValidationError(question.Code, $"“{question.Label}” must be a number."));
                    }

                    break;

                case QuestionTypes.Currency:
                    var quoted = MoneySubject.Quoting(question.Label);
                    var subject = new MoneySubject(question.ErrorSubject ?? quoted.Sentence, question.ErrorSubjectInline ?? quoted.Inline);
                    var currencyError = MoneyAnswer.Validate(trimmed, subject, question.MinValue);
                    if (currencyError is not null)
                    {
                        errors.Add(new AnswerValidationError(question.Code, currencyError));
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
                    var selected = AnswerVisibility.Split(trimmed);
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
        IReadOnlyDictionary<string, string?> answers,
        bool transitive) =>
        CollectPersistable(questions, answers, Visibility(questions, answers, transitive));

    internal static IReadOnlyList<(FormQuestion Question, string Value)> CollectPersistable(
        IReadOnlyList<FormQuestion> questions,
        IReadOnlyDictionary<string, string?> answers,
        AnswerVisibility visibility)
    {
        var result = new List<(FormQuestion Question, string Value)>();

        foreach (var question in questions)
        {
            if (!visibility.IsVisible(question.Code))
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
}