using Application.Common.Validation;
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
    private const int MaxTextLength = FieldLimits.AnswerText;

    // Which of these questions are visible, judged among themselves (AnswerVisibility). transitive is BLUM_9087: on, a
    // question whose parent is hidden is hidden too.
    public static AnswerVisibility Visibility(
        IEnumerable<ResolvedQuestion> questions,
        IReadOnlyDictionary<string, string?> answers,
        bool transitive) =>
        AnswerVisibility.Of(
            questions.Select(question => VisibilityNode.For(question.Code, question.ConditionalQuestionCode, question.ConditionalValue)),
            answers,
            transitive);

    public static IReadOnlyList<AnswerValidationError> Validate(
        IReadOnlyList<ResolvedQuestion> questions,
        IReadOnlyDictionary<string, string?> answers,
        bool enforceRequired,
        bool transitive) =>
        Validate(questions, answers, enforceRequired, Visibility(questions, answers, transitive));

    // For a caller whose visibility spans more than the questions it validates (a maturity page's rule hangs off an
    // earlier page). The visibility must have been built from the same answers.
    internal static IReadOnlyList<AnswerValidationError> Validate(
        IReadOnlyList<ResolvedQuestion> questions,
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
                    errors.Add(new AnswerValidationError(
                        question.Code,
                        question.ErrorMessage ?? $"Enter an answer for “{question.Label}”."));
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

            switch (question.QuestionType)
            {
                case QuestionTypes.Number:
                    ValidateNumber(question, trimmed, errors);
                    break;

                // Currency is a Domain.Forms.QuestionType and DcFormQuestion reuses that type, so a DHSC admin can
                // put one on a data-collection form module through the Form Designer API — the client's
                // DC_QUESTION_TYPES omits it, but the API is the enforcement point. Without this arm such an answer
                // would fall to the free-text default below and persist "abc" as a money value (BLUM-7860).
                case QuestionTypes.Currency:
                    var currencyError = MoneyAnswer.Validate(trimmed, MoneySubject.Quoting(question.Label), question.MinValue);
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
    public static IReadOnlyList<(ResolvedQuestion Question, string Value)> CollectPersistable(
        IReadOnlyList<ResolvedQuestion> questions,
        IReadOnlyDictionary<string, string?> answers,
        bool transitive)
    {
        var visibility = Visibility(questions, answers, transitive);
        var result = new List<(ResolvedQuestion Question, string Value)>();

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
}