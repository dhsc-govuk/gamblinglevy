using Application.Forms.Answers;

namespace Application.Forms.Rules;

// One answer option, as the guard sees it. A withdrawn option is still stored, so it is passed with IsActive false.
public sealed record RuleOption(string Value, bool IsActive = true);

// One question: whether it is a choice (radio, checkbox, select), whether LAs are shown it, its options, and its own
// show/hide rule as stored. A question on a page LAs are not shown is passed as inactive. Statuses is the activity
// statuses a results question is asked in; null means every status, and every set but the results questions passes null.
public sealed record RuleQuestion(
    string Code,
    bool IsChoice,
    bool IsActive,
    IReadOnlyList<RuleOption> Options,
    string? RuleQuestionCode = null,
    string? RuleValue = null,
    IReadOnlySet<string>? Statuses = null);

// One page, with its questions in display order and the page's own rule (maturity's VisibleWhen), as stored. Key names
// the page in messages and must name the same page before and after a change: a section's id or code, not its number.
public sealed record RulePage(string Key, IReadOnlyList<RuleQuestion> Questions, string? RuleQuestionCode = null, string? RuleValue = null);

// One form, with the pages LAs are shown in display order. Withdrawn questions go on any page, inactive. Codes are
// unique within a form, as every set already requires; were one repeated, its first question is the one rules name.
public sealed record RuleForm(IReadOnlyList<RulePage> Pages);

public enum RuleProblem
{
    HalfSet,
    NoValues,
    ParentMissing,
    ParentInactive,
    ParentNotChoice,
    ParentOnAnotherPage,
    ParentNotEarlier,
    ParentNotOnEarlierPage,
    ParentStatusesDoNotCover,
    ValueNotAnActiveOption,
    OptionValueHasComma,
}

// Owner is the question whose rule or option it is, or the page whose rule it is (OnPage). The rule is carried as
// written, and Value is the offending value: the rule value the parent does not offer, or the option value with a comma.
public sealed record RuleViolation(
    RuleProblem Problem,
    string Owner,
    bool OnPage,
    string? RuleQuestionCode,
    string? RuleValue,
    string? Value,
    string Message);

public enum RuleRefusal
{
    None,

    // 422 OPTION_VALUE_HAS_COMMA: an option value the change adds holds a comma.
    OptionValueHasComma,

    // 422 INVALID_CONDITIONAL: the rule the change writes is not valid.
    InvalidRule,

    // 409 QUESTION_REFERENCED: the change breaks another question's or page's rule.
    Referenced,
}

public sealed record RuleGuardOutcome(RuleRefusal Refusal, RuleViolation? Violation)
{
    public static readonly RuleGuardOutcome Allowed = new(RuleRefusal.None, null);

    public bool IsRefused => Refusal != RuleRefusal.None;

    public string? Message => Refusal == RuleRefusal.Referenced
        ? $"{Violation!.Message} Change or remove that rule first."
        : Violation?.Message;
}

// The one check that a form's show/hide rules are valid (BLUM-9087 spike item 8). Every set's question commands and
// publish go through it once wired in: the versioned forms in item 9, the activity record questions in item 12, and
// the results and maturity questions after them. It reads nothing and knows no flag; its callers run it with BLUM_9087
// on.
//
// A question's rule must name an active choice question earlier on the same page, because a page's save judges
// visibility against that page's answers. A page's rule must name an active choice question on an earlier page of the
// same form, because the maturity save merges in the answers saved before it. "Earlier" makes a cycle impossible. A
// results question's parent must be asked in every activity status the question is. Every value a rule names must be
// an active option of its parent, and a rule is both halves or neither. No active option value may hold a comma,
// because a checkbox answer and a rule's values are comma lists. A rule on an inactive question is never evaluated, so
// it is not judged.
//
// Check judges a whole form, as publish does, and lists every problem with each rule, so a problem a change adds to a
// rule that was already broken is still new. CheckChange refuses only the violations a change introduces, because the
// activity record editor accepted a parent anywhere in the form and saved rules may break these rules already: an edit
// that leaves such a rule as it was must still save. A rule is compared trimmed and with its values as a set, so one
// sent back in another order or spacing is the same rule; one rewritten by the change is judged afresh.
public static class ShowHideRuleGuard
{
    public static IReadOnlyList<RuleViolation> Check(RuleForm form)
    {
        var located = new Dictionary<string, Located>(StringComparer.Ordinal);
        for (var pageIndex = 0; pageIndex < form.Pages.Count; pageIndex++)
        {
            var questions = form.Pages[pageIndex].Questions;
            for (var position = 0; position < questions.Count; position++)
            {
                located.TryAdd(questions[position].Code, new Located(questions[position], pageIndex, position));
            }
        }

        var violations = new List<RuleViolation>();
        for (var pageIndex = 0; pageIndex < form.Pages.Count; pageIndex++)
        {
            var page = form.Pages[pageIndex];
            violations.AddRange(Judge(page.Key, onPage: true, page.RuleQuestionCode, page.RuleValue, pageIndex, position: -1, statuses: null, located));

            for (var position = 0; position < page.Questions.Count; position++)
            {
                var question = page.Questions[position];
                if (!question.IsActive)
                {
                    continue;
                }

                violations.AddRange(Judge(question.Code, onPage: false, question.RuleQuestionCode, question.RuleValue, pageIndex, position, question.Statuses, located));

                violations.AddRange(question.Options
                    .Where(option => option.IsActive && option.Value.Contains(','))
                    .Select(option => new RuleViolation(
                        RuleProblem.OptionValueHasComma,
                        question.Code,
                        OnPage: false,
                        RuleQuestionCode: null,
                        RuleValue: null,
                        option.Value,
                        $"The answer option value \"{option.Value}\" on \"{question.Code}\" contains a comma. Option values cannot contain commas.")));
            }
        }

        return violations;
    }

    // changedQuestionCode and changedPageKey name what the change writes: a question it creates or updates, or a page
    // whose rule it sets. The refusal is the one the admin can act on where they are: a comma in that question's options,
    // then that question's or page's own rule (InvalidRule), then a rule the change took its parent or value from
    // (Referenced). A reorder or a section change names neither.
    public static RuleGuardOutcome CheckChange(RuleForm before, RuleForm after, string? changedQuestionCode = null, string? changedPageKey = null)
    {
        var existing = Check(before).Select(Identity).ToHashSet();
        var introduced = Check(after).Where(violation => !existing.Contains(Identity(violation))).ToList();
        if (introduced.Count == 0)
        {
            return RuleGuardOutcome.Allowed;
        }

        bool IsOwn(RuleViolation violation) =>
            string.Equals(violation.Owner, violation.OnPage ? changedPageKey : changedQuestionCode, StringComparison.Ordinal);

        if (introduced.FirstOrDefault(violation => violation.Problem == RuleProblem.OptionValueHasComma && IsOwn(violation)) is { } ownComma)
        {
            return new RuleGuardOutcome(RuleRefusal.OptionValueHasComma, ownComma);
        }

        var rules = introduced.Where(violation => violation.Problem != RuleProblem.OptionValueHasComma).ToList();
        if (rules.FirstOrDefault(IsOwn) is { } own)
        {
            return new RuleGuardOutcome(RuleRefusal.InvalidRule, own);
        }

        return rules.Count > 0
            ? new RuleGuardOutcome(RuleRefusal.Referenced, rules[0])
            : new RuleGuardOutcome(RuleRefusal.OptionValueHasComma, introduced[0]);
    }

    // What makes two violations the same: the message is left out, because it quotes the rule as written.
    private static ViolationIdentity Identity(RuleViolation violation) => new(
        violation.Problem,
        violation.Owner,
        violation.OnPage,
        violation.RuleQuestionCode,
        string.Join(',', AnswerVisibility.Split(violation.RuleValue ?? string.Empty).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)),
        violation.Value);

    // Everything wrong with one rule, none when it is valid or there is none. position is -1 for a page's rule. Once the
    // parent is found, each problem is reported on its own, so a change that adds one to a rule already broken is seen.
    private static IEnumerable<RuleViolation> Judge(
        string owner,
        bool onPage,
        string? ruleQuestionCode,
        string? ruleValue,
        int pageIndex,
        int position,
        IReadOnlySet<string>? statuses,
        IReadOnlyDictionary<string, Located> located)
    {
        var codeBlank = string.IsNullOrWhiteSpace(ruleQuestionCode);
        var valueBlank = string.IsNullOrWhiteSpace(ruleValue);
        if (codeBlank && valueBlank)
        {
            yield break;
        }

        var subject = onPage ? $"The show/hide rule on page \"{owner}\"" : $"The show/hide rule on \"{owner}\"";
        RuleViolation Violation(RuleProblem problem, string message, string? value = null) =>
            new(problem, owner, onPage, ruleQuestionCode, ruleValue, value, message);

        if (codeBlank || valueBlank)
        {
            yield return Violation(RuleProblem.HalfSet, $"{subject} needs both a question and at least one answer, or neither.");
            yield break;
        }

        var parentCode = ruleQuestionCode!;
        var values = AnswerVisibility.Split(ruleValue!);
        if (values.Length == 0)
        {
            yield return Violation(RuleProblem.NoValues, $"{subject} names no answer.");
            yield break;
        }

        if (!onPage && string.Equals(parentCode, owner, StringComparison.Ordinal))
        {
            yield return Violation(RuleProblem.ParentNotEarlier, $"\"{owner}\" cannot depend on itself.");
            yield break;
        }

        if (!located.TryGetValue(parentCode, out var parent))
        {
            yield return Violation(RuleProblem.ParentMissing, $"{subject} depends on \"{parentCode}\", which is not on this form.");
            yield break;
        }

        if (!parent.Question.IsActive)
        {
            yield return Violation(RuleProblem.ParentInactive, $"{subject} depends on \"{parentCode}\", which has been withdrawn.");
        }

        if (!parent.Question.IsChoice)
        {
            yield return Violation(RuleProblem.ParentNotChoice, $"{subject} depends on \"{parentCode}\", which is not a radio, checkbox or select question.");
        }

        if (onPage)
        {
            if (parent.PageIndex >= pageIndex)
            {
                yield return Violation(RuleProblem.ParentNotOnEarlierPage, $"{subject} depends on \"{parentCode}\", which is not on an earlier page.");
            }
        }
        else
        {
            if (parent.PageIndex != pageIndex)
            {
                yield return Violation(RuleProblem.ParentOnAnotherPage, $"{subject} depends on \"{parentCode}\", which is on another page. A question can only depend on a question on its own page.");
            }
            else if (parent.Position >= position)
            {
                yield return Violation(RuleProblem.ParentNotEarlier, $"{subject} depends on \"{parentCode}\", which comes after it. A question can only depend on a question before it.");
            }

            if (parent.Question.Statuses is not null && (statuses is null || !statuses.IsSubsetOf(parent.Question.Statuses)))
            {
                yield return Violation(RuleProblem.ParentStatusesDoNotCover, $"{subject} depends on \"{parentCode}\", which is not asked for every activity status \"{owner}\" is.");
            }
        }

        // A question that is not a choice offers no values, which ParentNotChoice already says.
        if (!parent.Question.IsChoice)
        {
            yield break;
        }

        var offered = parent.Question.Options
            .Where(option => option.IsActive)
            .Select(option => option.Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var notOffered in values.Distinct(StringComparer.Ordinal).Where(value => !offered.Contains(value)))
        {
            yield return Violation(RuleProblem.ValueNotAnActiveOption, $"{subject} shows it for \"{notOffered}\", which is not an answer option of \"{parentCode}\".", notOffered);
        }
    }

    private sealed record ViolationIdentity(RuleProblem Problem, string Owner, bool OnPage, string? RuleQuestionCode, string RuleValues, string? Value);

    private sealed record Located(RuleQuestion Question, int PageIndex, int Position);
}