namespace Application.Forms.Answers;

// A show/hide rule: show when the answer to QuestionCode holds any of Values. A checkbox parent's answer is itself a
// comma list, so any one of its ticks is enough.
public sealed record VisibilityRule(string QuestionCode, IReadOnlyList<string> Values)
{
    // The stored ConditionalQuestionCode / ConditionalValue pair. Either half blank means no rule; the value is a comma
    // list, each part trimmed.
    public static VisibilityRule? From(string? questionCode, string? values) =>
        string.IsNullOrWhiteSpace(questionCode) || string.IsNullOrWhiteSpace(values)
            ? null
            : new VisibilityRule(questionCode, AnswerVisibility.Split(values));
}

// One question as the visibility check sees it: its code, its own rule, and the rule of the page it sits on.
public sealed record VisibilityNode(string Code, VisibilityRule? Rule, VisibilityRule? PageRule = null)
{
    public static VisibilityNode For(string code, string? conditionalQuestionCode, string? conditionalValue, VisibilityRule? pageRule = null) =>
        new(code, VisibilityRule.From(conditionalQuestionCode, conditionalValue), pageRule);
}

// The one place that decides which questions, and which pages, are visible (BLUM-9087 spike item 7). Every answer
// validator, the report and activity-record presenters, the snapshots and the offline file read it.
//
// A rule holds when its parent's answer is not blank and any of its values is in the rule's list. A question is
// visible when its page's rule and its own rule both hold.
//
// transitive: false judges every rule against the answers as given — one level, the check every copy made before, and
// what the client's isQuestionVisible does. transitive: true (BLUM_9087 on) first removes the answer of every hidden
// question, again and again until nothing changes, and then judges the rules against what is left, so a question whose
// parent is hidden is hidden too, and so is a page whose rule's parent is. That is the maturity client's
// pruneHiddenAnswers, so a cycle whose rules all hold stays visible there as here.
//
// A parent that is not among the questions given is judged on its answer alone, in both modes: a page's save sees only
// that page's questions unless its caller passes more. A code that is not among them is visible.
public sealed class AnswerVisibility
{
    private readonly IReadOnlyDictionary<string, string?> shown;
    private readonly IReadOnlySet<string> hidden;

    private AnswerVisibility(IReadOnlyDictionary<string, string?> shown, IReadOnlySet<string> hidden)
    {
        this.shown = shown;
        this.hidden = hidden;
    }

    public static AnswerVisibility Of(IEnumerable<VisibilityNode> questions, IReadOnlyDictionary<string, string?> answers, bool transitive)
    {
        // Every caller's set holds one question per code; were a code repeated, it is visible if any of its questions is.
        var byCode = questions.GroupBy(question => question.Code, StringComparer.Ordinal).ToList();
        var shown = new Dictionary<string, string?>(answers, StringComparer.Ordinal);

        if (transitive)
        {
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var group in byCode)
                {
                    if (shown.TryGetValue(group.Key, out var value) && !string.IsNullOrWhiteSpace(value) && !group.Any(question => IsVisible(question, shown)))
                    {
                        shown.Remove(group.Key);
                        changed = true;
                    }
                }
            }
        }

        var hidden = byCode
            .Where(group => !group.Any(question => IsVisible(question, shown)))
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

        return new AnswerVisibility(shown, hidden);
    }

    public bool IsVisible(string questionCode) => !hidden.Contains(questionCode);

    public bool IsPageVisible(VisibilityRule? rule) => Holds(rule, shown);

    // The comma list every rule value and every stored checkbox answer is, each part trimmed. The validators read a
    // checkbox answer through it too, so its ticks and a rule's values are always split alike.
    internal static string[] Split(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IsVisible(VisibilityNode question, IReadOnlyDictionary<string, string?> answers) =>
        Holds(question.PageRule, answers) && Holds(question.Rule, answers);

    private static bool Holds(VisibilityRule? rule, IReadOnlyDictionary<string, string?> answers)
    {
        if (rule is null)
        {
            return true;
        }

        var parentAnswer = answers.TryGetValue(rule.QuestionCode, out var value) ? value : null;
        if (string.IsNullOrWhiteSpace(parentAnswer))
        {
            return false;
        }

        return Split(parentAnswer).Any(rule.Values.Contains);
    }
}