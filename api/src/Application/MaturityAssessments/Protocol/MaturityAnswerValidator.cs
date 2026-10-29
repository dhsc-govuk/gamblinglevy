using Application.Forms.Answers;
using Application.Submissions.Protocol;

namespace Application.MaturityAssessments.Protocol;

// Validates maturity-assessment answers. Defers to SubmissionAnswerValidator for everything the quarterly report
// already checks (required-on-complete, option membership) and to AnswerVisibility for which questions count, and
// adds the two things the annual assessment has that the quarterly one does not: page-level visibility (a hidden
// page's questions are neither required nor persisted) and the "Funded roles" JSON repeater.
//
// Free text carries no protocol character limit (BLUM-8411). The one bound left is FieldLimits.AnswerText, and it
// applies to a draft save and Continue alike: the draft exemption the old 500 limit had is gone, so a value past
// it — pasted in, or carried in by an imported 2025 answer — blocks "Save and come back later" too until the LA
// shortens it. The client flags that value on the field before the request is sent (MATURITY_ANSWER_MAX_LENGTH).
public static class MaturityAnswerValidator
{
    // Which of these questions are visible, each under its page's rule as well as its own (AnswerVisibility). Page and
    // question rules stay within a section, so a section's questions are enough to judge any of them. transitive is
    // BLUM_9087: on, a question or page whose parent is hidden is hidden too, which is what the page shows
    // (pruneHiddenAnswers).
    public static AnswerVisibility Visibility(
        IEnumerable<MaturityResolvedQuestion> questions,
        IReadOnlyDictionary<string, string?> answers,
        bool transitive) =>
        AnswerVisibility.Of(
            questions.Select(question => VisibilityNode.For(
                question.Code, question.ConditionalQuestionCode, question.ConditionalValue, PageRule(question.PageVisibleWhen))),
            answers,
            transitive);

    public static bool IsPageVisible(AnswerVisibility visibility, MaturityPageVisibility? rule) =>
        visibility.IsPageVisible(PageRule(rule));

    public static IReadOnlyList<AnswerValidationError> Validate(
        IReadOnlyList<MaturityResolvedQuestion> questions,
        IReadOnlyDictionary<string, string?> answers,
        bool enforceRequired,
        bool transitive) =>
        Validate(questions, answers, enforceRequired, Visibility(questions, answers, transitive));

    // For a page save, which validates the page's questions but judges them among the whole section's: a page's rule
    // hangs off an earlier page. The visibility must have been built from the same answers.
    internal static IReadOnlyList<AnswerValidationError> Validate(
        IReadOnlyList<MaturityResolvedQuestion> questions,
        IReadOnlyDictionary<string, string?> answers,
        bool enforceRequired,
        AnswerVisibility visibility)
    {
        var standard = questions.Where(q => !q.IsRoles).ToList<ResolvedQuestion>();

        var errors = SubmissionAnswerValidator.Validate(standard, answers, enforceRequired, visibility).ToList();

        // A checkbox's exclusive "None" (MA_NONFUNDED_ROLES) stands alone too. The page unticks every other box when
        // it is ticked, so the server never saw the two together until a request came from somewhere else and was
        // stored as given (BLUM-9084). One beside another value is refused like a value outside the list, on a draft
        // as well. A question already refused for its options keeps that one error.
        foreach (var question in questions.Where(q => q.QuestionType == MaturityFieldTypes.Checkbox))
        {
            if (question.ExclusiveOptionValue is not { } exclusive
                || !visibility.IsVisible(question.Code)
                || !answers.TryGetValue(question.Code, out var raw)
                || string.IsNullOrWhiteSpace(raw)
                || errors.Any(e => e.QuestionCode == question.Code))
            {
                continue;
            }

            var selected = AnswerVisibility.Split(raw);
            if (selected.Contains(exclusive) && selected.Any(value => value != exclusive))
            {
                errors.Add(new AnswerValidationError(question.Code, $"Select valid options for “{question.Label}”."));
            }
        }

        foreach (var question in questions.Where(q => q.IsRoles))
        {
            if (!visibility.IsVisible(question.Code))
            {
                continue;
            }

            var value = answers.TryGetValue(question.Code, out var raw) ? raw : null;
            if (!MaturityRolesAnswer.TryParse(value, out var entries))
            {
                errors.Add(new AnswerValidationError(question.Code, $"“{question.Label}” is not in the expected format."));
                continue;
            }

            if (entries.Count == 0)
            {
                if (enforceRequired && question.IsRequired)
                {
                    errors.Add(new AnswerValidationError(question.Code, question.ErrorMessage ?? "Add at least one role"));
                }

                continue;
            }

            if (entries.Count > MaturityRolesAnswer.MaxRoles)
            {
                errors.Add(new AnswerValidationError(question.Code, $"Add no more than {MaturityRolesAnswer.MaxRoles} roles."));
                continue;
            }

            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                var roleLabel = $"role {index + 1}";

                // A draft ("Save and come back later") keeps a half-finished row — the client waives the same three
                // checks under enforceRequired = false — but a value that is not in the option list is refused either
                // way, exactly as the shared validator treats a choice question.
                var hasRole = !string.IsNullOrWhiteSpace(entry.Role);
                var hasTime = !string.IsNullOrWhiteSpace(entry.Time);

                // The exclusive "None" (BLUM-9112) stands alone and asks no time: the client offers it only while there
                // is one row, so one beside another role is refused like any role outside the list.
                if (MaturityRolesAnswer.IsExclusive(entry, question.ExclusiveOptionValue))
                {
                    if (entries.Count > 1)
                    {
                        errors.Add(new AnswerValidationError(question.Code, $"Select a valid role for {roleLabel}."));
                    }

                    continue;
                }

                if ((hasRole && question.Options.All(o => o.Value != entry.Role)) || (!hasRole && enforceRequired))
                {
                    errors.Add(new AnswerValidationError(question.Code, $"Select a valid role for {roleLabel}."));
                }

                if ((hasTime && question.SecondaryOptions.All(o => o.Value != entry.Time)) || (!hasTime && enforceRequired))
                {
                    errors.Add(new AnswerValidationError(question.Code, $"Select how much of the staff member's time the funding is paying for {roleLabel}."));
                }

                if (enforceRequired && entry.Role == MaturityOptionValues.Other && string.IsNullOrWhiteSpace(entry.OtherRole))
                {
                    errors.Add(new AnswerValidationError(question.Code, $"Enter the name of the other role for {roleLabel}."));
                }

                if (entry.OtherRole is { Length: > MaturityRolesAnswer.MaxOtherRoleLength })
                {
                    errors.Add(new AnswerValidationError(question.Code, $"The other role name for {roleLabel} must be {MaturityRolesAnswer.MaxOtherRoleLength} characters or less."));
                }
            }
        }

        return errors;
    }

    // The answers that should actually be persisted: visible questions on visible pages with a non-empty value.
    // Roles values are re-serialised canonically so every stored repeater has one shape.
    public static IReadOnlyList<(MaturityResolvedQuestion Question, string Value)> CollectPersistable(
        IReadOnlyList<MaturityResolvedQuestion> questions,
        IReadOnlyDictionary<string, string?> answers,
        bool transitive) =>
        CollectPersistable(questions, answers, Visibility(questions, answers, transitive));

    internal static IReadOnlyList<(MaturityResolvedQuestion Question, string Value)> CollectPersistable(
        IReadOnlyList<MaturityResolvedQuestion> questions,
        IReadOnlyDictionary<string, string?> answers,
        AnswerVisibility visibility)
    {
        var result = new List<(MaturityResolvedQuestion Question, string Value)>();

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

            if (question.IsRoles)
            {
                if (!MaturityRolesAnswer.TryParse(value, out var entries) || entries.Count == 0)
                {
                    continue;
                }

                result.Add((question, MaturityRolesAnswer.Serialize(MaturityRolesAnswer.Canonical(entries, question.ExclusiveOptionValue))));
                continue;
            }

            result.Add((question, value.Trim()));
        }

        return result;
    }

    private static VisibilityRule? PageRule(MaturityPageVisibility? rule) =>
        rule is null ? null : new VisibilityRule(rule.QuestionCode, rule.Values);
}