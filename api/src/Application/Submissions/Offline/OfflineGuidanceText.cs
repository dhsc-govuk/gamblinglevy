using Application.Submissions.Protocol;
using Domain.Forms;

namespace Application.Submissions.Offline;

// The "how to answer" text in row 2 of an offline file. A spreadsheet cannot show or hide a question the way the web
// form does, so every question is in the file at once and this text carries the conditional logic instead: the
// question a follow-up hangs off says which answer leads to it ("If you answer ‘No’, leave Q9 blank and go to Q10"),
// and the follow-up says when to answer it ("Only answer if you chose ‘Other’ in Q7 (column K)").
public static class OfflineGuidanceText
{
    // A parent with more follow-ups than this summarises them as a range instead of one sentence each. The steering
    // group's members question has 24 (an "Other" box and one engagement question per member).
    private const int MaxFollowUpsListed = 3;

    public sealed record Parent(OfflineColumn Column, ResolvedQuestion Question);

    public sealed record FollowUp(OfflineColumn Column, ResolvedQuestion Question, bool NotApplicableOnSomeRows);

    public static string ForQuestion(
        ResolvedQuestion question,
        string? note,
        IReadOnlyList<Parent> parents,
        IReadOnlyList<FollowUp> followUps,
        OfflineColumn? nextAfterFollowUps)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(note))
        {
            parts.Add(note.Trim());
        }

        parts.Add(Condition(question, parents));

        var format = Format(question);
        if (format.Length > 0)
        {
            parts.Add(format);
        }

        parts.AddRange(FollowUps(question, followUps, nextAfterFollowUps));

        if (!string.IsNullOrWhiteSpace(question.Hint))
        {
            parts.Add(EndSentence(question.Hint.Trim()));
        }

        return string.Join(" ", parts);
    }

    // The trigger answers of a follow-up, as its parent's labels: "‘Other’", "‘Police’ or ‘Local charities’".
    public static string TriggerLabels(ResolvedQuestion followUp, ResolvedQuestion parent) =>
        string.Join(" or ", TriggerValues(followUp).Select(value => $"‘{LabelFor(parent, value)}’"));

    // "chose ‘Other’ in" for a list or a drop-down, "answered ‘Yes’ to" for a single choice.
    public static string ChoseVerb(ResolvedQuestion parent) =>
        parent.QuestionType == QuestionTypes.Radio ? "answered" : "chose";

    public static string ChosePreposition(ResolvedQuestion parent) =>
        parent.QuestionType == QuestionTypes.Radio ? "to" : "in";

    public static string Format(ResolvedQuestion question) => FormatOrNull(question) ?? string.Empty;

    private static string? FormatOrNull(ResolvedQuestion question) => question.QuestionType switch
    {
        QuestionTypes.Radio or QuestionTypes.Select when question.Options.Count == 2 =>
            $"Enter ‘{question.Options[0].Label}’ or ‘{question.Options[1].Label}’.",
        QuestionTypes.Radio or QuestionTypes.Select =>
            $"Enter one of: {string.Join("; ", question.Options.Select(option => $"‘{option.Label}’"))}.",
        QuestionTypes.Checkbox =>
            $"Enter one or more of these, separated by a semicolon (;): {string.Join("; ", question.Options.Select(option => $"‘{option.Label}’"))}.",
        QuestionTypes.Number when question.IsPercentage => "Enter a percentage between 0 and 100, like 25.",
        QuestionTypes.Number => NumberFormat(question),
        QuestionTypes.Currency => "Enter a whole number of pounds, like 1500. Enter 0 if you spent nothing.",
        _ => question.MaxLength is { } maxLength ? $"{maxLength} characters or less." : null,
    };

    private static string Condition(ResolvedQuestion question, IReadOnlyList<Parent> parents)
    {
        if (parents.Count == 0)
        {
            return question.IsRequired ? "Required." : "Optional.";
        }

        // Every parent column carries the same question in a different wording, so the first one names the answers.
        var parent = parents[0].Question;
        var where = string.Join(" or ", parents.Select(p => p.Column.Name));
        var when = $"Only answer if you {ChoseVerb(parent)} {TriggerLabels(question, parent)} {ChosePreposition(parent)} {where}";

        return question.IsRequired
            ? $"{when} – an answer is then required. Otherwise leave it blank."
            : $"Optional. {when}. Otherwise leave it blank.";
    }

    private static IEnumerable<string> FollowUps(ResolvedQuestion parent, IReadOnlyList<FollowUp> followUps, OfflineColumn? nextAfterFollowUps)
    {
        if (followUps.Count == 0)
        {
            yield break;
        }

        if (followUps.Count > MaxFollowUpsListed)
        {
            var first = followUps.MinBy(followUp => followUp.Column.Index)!.Column;
            var last = followUps.MaxBy(followUp => followUp.Column.Index)!.Column;
            yield return $"Some answers need a follow-up in {first.ShortName} to {last.ShortName} (columns {first.Letter} to {last.Letter}). "
                + "For each answer you choose, answer the question that names it, and leave the others blank.";
            yield break;
        }

        foreach (var followUp in followUps)
        {
            var triggers = TriggerLabels(followUp.Question, parent);
            var unlessNotApplicable = followUp.NotApplicableOnSomeRows ? ", unless it says N/A" : string.Empty;

            if (parent.QuestionType == QuestionTypes.Radio && followUps.Count == 1)
            {
                // A single choice with one follow-up reads as a skip: the other answers jump past it.
                var others = parent.Options
                    .Where(option => !TriggerValues(followUp.Question).Contains(option.Value))
                    .Select(option => $"‘{option.Label}’")
                    .ToList();
                var skip = others.Count == 0
                    ? string.Empty
                    : nextAfterFollowUps is null
                        ? $" If you answer {string.Join(" or ", others)}, leave {followUp.Column.ShortName} blank."
                        : $" If you answer {string.Join(" or ", others)}, leave {followUp.Column.ShortName} blank and go to {nextAfterFollowUps.Name}.";

                yield return $"If you answer {triggers}, answer {followUp.Column.Name} next{unlessNotApplicable}.{skip}";
                continue;
            }

            var verb = parent.QuestionType == QuestionTypes.Radio ? "answer" : "choose";
            yield return $"If you {verb} {triggers}, also answer {followUp.Column.Name}{unlessNotApplicable}. "
                + $"If not, leave {followUp.Column.ShortName} blank.";
        }
    }

    private static string NumberFormat(ResolvedQuestion question)
    {
        var text = "Enter a whole number, like 12.";
        return question.MinValue is { } min ? $"{text} It must be {min:0} or more." : text;
    }

    private static IReadOnlyList<string> TriggerValues(ResolvedQuestion followUp) =>
        (followUp.ConditionalValue ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string LabelFor(ResolvedQuestion parent, string value) =>
        parent.Options.FirstOrDefault(option => option.Value == value)?.Label ?? value;

    private static string EndSentence(string text) =>
        text.EndsWith('.') || text.EndsWith('?') || text.EndsWith('!') ? text : text + ".";
}