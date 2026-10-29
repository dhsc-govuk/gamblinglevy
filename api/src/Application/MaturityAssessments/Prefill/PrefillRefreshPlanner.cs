using Application.MaturityAssessments.Protocol;
using Domain.MaturityAssessments;

namespace Application.MaturityAssessments.Prefill;

// What one refresh of an already-started assessment changes: the stored rows to remove and the quarterly values
// written in their place. A row is only ever removed together with its replacement.
public sealed record PrefillRefreshPlan(
    IReadOnlyList<MaturityAssessmentAnswerId> Remove,
    IReadOnlyList<MaturityPrefilledAnswer> Write)
{
    public bool IsEmpty => Write.Count == 0;
}

// Which quarterly-derived values may be written into an assessment the LA has ALREADY started (BLUM-8927). Start
// pre-fills once, so a quarterly report submitted afterwards was never pulled through; the refresh that follows a
// submit closes that gap without taking anything away from the LA:
//   - a Completed section is left alone — the LA has checked its answers and signed it off;
//   - a page holding any user-sourced answer is left alone. A page save replaces every code on the page, so one
//     user row means the LA saved it, and a blank beside it is a value they cleared (the un-clearable value
//     ActivityResultCarryForward documents);
//   - everywhere else a quarterly value fills a blank or replaces a row that is still pre-filled.
// Only quarterly values are written and nothing is removed without a replacement: MaturityPrefillService swallows a
// failing quarterly read and returns nothing for it, so syncing to "whatever was recomputed" would wipe pre-filled
// answers, or downgrade them to the baseline, on a transient error.
public static class PrefillRefreshPlanner
{
    public static PrefillRefreshPlan Plan(
        IReadOnlyList<MaturityResolvedQuestion> questions,
        IReadOnlySet<string> completedSections,
        IReadOnlyList<MaturityAssessmentAnswer> existing,
        IReadOnlyList<MaturityPrefilledAnswer> prefilled)
    {
        var existingByCode = existing
            .GroupBy(a => a.QuestionCode, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var quarterly = prefilled
            .Where(p => p.Source.Equals(AnswerSource.Quarterly) && !string.IsNullOrWhiteSpace(p.Value))
            .GroupBy(p => p.QuestionCode, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);

        var savedPages = questions
            .Where(q => existingByCode.TryGetValue(q.Code, out var row) && row.Source.IsUser)
            .Select(q => (q.Section, q.Page))
            .ToHashSet();

        var remove = new List<MaturityAssessmentAnswerId>();
        var write = new List<MaturityPrefilledAnswer>();

        foreach (var question in questions)
        {
            if (completedSections.Contains(question.Section)
                || savedPages.Contains((question.Section, question.Page))
                || !quarterly.TryGetValue(question.Code, out var value))
            {
                continue;
            }

            if (existingByCode.TryGetValue(question.Code, out var stored))
            {
                if (stored.Source.Equals(AnswerSource.Quarterly) && string.Equals(stored.Value, value, StringComparison.Ordinal))
                {
                    continue;
                }

                remove.Add(stored.Id);
            }

            write.Add(new MaturityPrefilledAnswer(question.Code, value, AnswerSource.Quarterly));
        }

        return new PrefillRefreshPlan(remove, write);
    }
}