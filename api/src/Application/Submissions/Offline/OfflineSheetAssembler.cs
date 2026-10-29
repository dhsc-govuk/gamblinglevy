using Application.Submissions.Protocol;

namespace Application.Submissions.Offline;

// A column as a section builder describes it, before it has a position, a question number or guidance.
public sealed record OfflineColumnSpec(
    string Key,
    OfflineColumnKind Kind,
    string Heading,

    // Question columns: the question as the first row it applies to asks it. Its label is the heading.
    ResolvedQuestion? Prototype = null,

    // Question columns: a sentence placed in front of the generated guidance ("For Active activities.").
    string? Note = null,

    // Every other column: the whole guidance cell.
    string? Guidance = null);

// Turns a section's column specs and rows into an OfflineSheet: positions and question numbers (Q1..Qn over the
// question columns only, left to right), and each question column's guidance — including the conditional helper
// text, worked out from which follow-up hangs off which question in the rows that actually use them.
public static class OfflineSheetAssembler
{
    public static OfflineSheet Assemble(
        string moduleCode,
        Guid collectionId,
        Guid organisationId,
        string fingerprint,
        IReadOnlyList<OfflineColumnSpec> specs,
        IReadOnlyList<OfflineRow> rows,
        bool singleRow)
    {
        var positioned = new List<OfflineColumn>();
        var number = 0;
        for (var index = 0; index < specs.Count; index++)
        {
            var spec = specs[index];
            int? questionNumber = spec.Kind == OfflineColumnKind.Question ? ++number : null;
            var heading = questionNumber is { } n ? $"Q{n}. {spec.Heading}" : spec.Heading;
            positioned.Add(new OfflineColumn(spec.Key, spec.Kind, heading, spec.Guidance ?? string.Empty, index, questionNumber));
        }

        var byKey = positioned.ToDictionary(column => column.Key, StringComparer.Ordinal);
        var links = FollowUpLinks(rows);

        var columns = positioned
            .Select((column, index) =>
            {
                var spec = specs[index];
                if (spec.Kind != OfflineColumnKind.Question || spec.Prototype is null)
                {
                    return column;
                }

                var parents = links
                    .Where(link => link.ChildKey == column.Key)
                    .Select(link => link.ParentKey)
                    .Distinct()
                    .Select(key => byKey[key])
                    .OrderBy(parent => parent.Index)
                    .Select(parent => new OfflineGuidanceText.Parent(parent, specs[parent.Index].Prototype!))
                    .ToList();

                var followUps = links
                    .Where(link => link.ParentKey == column.Key)
                    .Select(link => link.ChildKey)
                    .Distinct()
                    .Select(key => byKey[key])
                    .OrderBy(child => child.Index)
                    .Select(child => new OfflineGuidanceText.FollowUp(
                        child,
                        specs[child.Index].Prototype!,
                        rows.Any(row => row.BindingFor(column.Key) is not null && row.BindingFor(child.Key) is null)))
                    .ToList();

                // "Go to" names the next question the same activities are asked, not merely the next column: in a
                // file mixing activity types the column after a follow-up often belongs to another type and says N/A.
                int? lastFollowUp = followUps.Count == 0 ? null : followUps.Max(followUp => followUp.Column.Index);
                var next = lastFollowUp is null
                    ? null
                    : positioned.FirstOrDefault(candidate =>
                        candidate.Index > lastFollowUp
                        && candidate.Kind == OfflineColumnKind.Question
                        && rows.Any(row => row.BindingFor(column.Key) is not null && row.BindingFor(candidate.Key) is not null));

                return column with
                {
                    Guidance = OfflineGuidanceText.ForQuestion(spec.Prototype, spec.Note, parents, followUps, next),
                };
            })
            .ToList();

        return new OfflineSheet(moduleCode, collectionId, organisationId, fingerprint, columns, rows, singleRow);
    }

    // Parent → follow-up links, taken row by row: a follow-up's parent is the column that asks its parent question in
    // that same row. That is what pairs a Finished-only follow-up with a parent asked of Closed and Finished
    // activities alike, where the two sit in differently keyed columns.
    private static IReadOnlyList<(string ParentKey, string ChildKey)> FollowUpLinks(IReadOnlyList<OfflineRow> rows)
    {
        var links = new HashSet<(string ParentKey, string ChildKey)>();
        foreach (var row in rows)
        {
            foreach (var binding in row.Bindings)
            {
                var parentCode = binding.Question.ConditionalQuestionCode;
                if (string.IsNullOrWhiteSpace(parentCode) || string.IsNullOrWhiteSpace(binding.Question.ConditionalValue))
                {
                    continue;
                }

                var parent = row.BindingForCode(parentCode);
                if (parent is not null)
                {
                    links.Add((parent.ColumnKey, binding.ColumnKey));
                }
            }
        }

        return links.ToList();
    }
}