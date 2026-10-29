using Domain.Activities;
using Domain.Users;

namespace Application.Activities.Commands;

// Diff/merges the expected results submitted with an activity update against the activity's existing rows,
// preserving row identity wherever a row survives the edit. Ids must stay stable because the quarterly report
// keys each custom ("Other") progress answer on CUSTOM_{rowId:N} — recreating rows orphans saved report answers
// and blocks completing the activity report (BLUM-6805). Standard rows match by KeyResultCode; custom rows match
// by exact text first (multiset, consumed in display order), then by position so an in-place rewording keeps its
// id — never across positions, so a deleted result's saved answer cannot resurface under an unrelated new one.
// Pure: the plan carries pending amendments; callers apply them via ActivityExpectedResult.Amend.
public static class ExpectedResultSync
{
    public sealed record Amendment(ActivityExpectedResult Row, string Text, int DisplayOrder);

    public sealed record SyncPlan(
        IReadOnlyList<ActivityExpectedResult> ToAdd,
        IReadOnlyList<Amendment> ToUpdate,
        IReadOnlyList<ActivityExpectedResult> ToRemove);

    public static SyncPlan Plan(
        IReadOnlyList<ActivityExpectedResult> existing,
        IReadOnlyList<(string? KeyResultCode, string Text)> incoming,
        ActivityId activityId,
        UserId actorId,
        DateTime utcNow)
    {
        // Standards group by code rather than ToDictionary: concurrent edits can plant duplicate rows for one
        // code (there is no unique index on it), and grouping lets the surplus fall through to ToRemove so the
        // next successful edit self-heals instead of throwing on every attempt.
        var standardsByCode = existing
            .Where(r => !r.IsCustom)
            .GroupBy(r => r.KeyResultCode!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.DisplayOrder).ToList(), StringComparer.Ordinal);

        // Ordinal = position among the activity's custom rows; pass 2 only pairs equal ordinals.
        var existingCustoms = existing
            .Where(r => r.IsCustom)
            .OrderBy(r => r.DisplayOrder)
            .Select((row, ordinal) => (Row: row, Ordinal: ordinal))
            .ToList();

        var toAdd = new List<ActivityExpectedResult>();
        var pairs = new List<(ActivityExpectedResult Row, string Text, int Order)>();
        var incomingCustoms = new List<(string Text, int Order, int Ordinal)>();

        for (var order = 0; order < incoming.Count; order++)
        {
            var (keyResultCode, text) = incoming[order];
            if (keyResultCode is not null)
            {
                if (standardsByCode.TryGetValue(keyResultCode, out var rows))
                {
                    pairs.Add((rows[0], text, order));
                    rows.RemoveAt(0);
                    if (rows.Count == 0)
                    {
                        standardsByCode.Remove(keyResultCode);
                    }
                }
                else
                {
                    toAdd.Add(ActivityExpectedResult.Record(
                        ActivityExpectedResultId.New(), activityId, text, order, actorId, utcNow, keyResultCode));
                }
            }
            else
            {
                incomingCustoms.Add((text, order, incomingCustoms.Count));
            }
        }

        // Custom pass 1 — exact text, consuming existing rows in display order so duplicate texts pair one-to-one.
        var reworded = new List<(string Text, int Order, int Ordinal)>();
        foreach (var (text, order, ordinal) in incomingCustoms)
        {
            var matchIndex = existingCustoms.FindIndex(e => string.Equals(e.Row.Text, text, StringComparison.Ordinal));
            if (matchIndex >= 0)
            {
                pairs.Add((existingCustoms[matchIndex].Row, text, order));
                existingCustoms.RemoveAt(matchIndex);
            }
            else
            {
                reworded.Add((text, order, ordinal));
            }
        }

        // Custom pass 2 — in-place rewordings only: a leftover incoming custom takes over a leftover existing
        // row (keeping its id, and therefore its saved CUSTOM_{id} answer) only when both occupy the same
        // position among the activity's customs. A removed entry and an added entry at different positions never
        // pair, so a deleted result's answer cannot be transplanted onto an unrelated new one.
        foreach (var (text, order, ordinal) in reworded)
        {
            var matchIndex = existingCustoms.FindIndex(e => e.Ordinal == ordinal);
            if (matchIndex >= 0)
            {
                pairs.Add((existingCustoms[matchIndex].Row, text, order));
                existingCustoms.RemoveAt(matchIndex);
            }
            else
            {
                toAdd.Add(ActivityExpectedResult.Record(
                    ActivityExpectedResultId.New(), activityId, text, order, actorId, utcNow));
            }
        }

        var toUpdate = pairs
            .Where(p => !string.Equals(p.Row.Text, p.Text, StringComparison.Ordinal) || p.Row.DisplayOrder != p.Order)
            .Select(p => new Amendment(p.Row, p.Text, p.Order))
            .ToList();

        var toRemove = standardsByCode.Values
            .SelectMany(rows => rows)
            .Concat(existingCustoms.Select(e => e.Row))
            .ToList();

        return new SyncPlan(toAdd, toUpdate, toRemove);
    }
}