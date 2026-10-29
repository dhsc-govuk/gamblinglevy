using Application.MaturityAssessments.Protocol;
using Application.MaturityAssessments.Snapshots;

namespace Application.MaturityAssessments.Export;

// Pure row builders for the two maturity-assessment exports. Both read the frozen snapshot, never live answers, so
// an export of a submitted assessment is byte-stable whatever happens to the catalogue or the quarterly data later.
//
// LONG format (one organisation's own ZIP): one row per visible question — Section, Page, Question, Answer.
// WIDE format (DHSC consolidated ZIP): one row per submitting organisation, one column per catalogue question in
// catalogue order; matrix items become their own columns, and the funded-roles repeater is flattened into
// "Role n: role" / "Role n: time" column pairs up to the widest submitter. Hidden or unanswered questions are blank.
public static class MaturityAssessmentCsv
{
    public sealed record WideColumn(string Header, string Code, int? RoleIndex, string? RoleField);

    public static IEnumerable<IReadOnlyList<string>> LongRows(IReadOnlyList<string> prefix, MaturityAssessmentSnapshotDocument snapshot)
    {
        foreach (var section in snapshot.Sections)
        {
            foreach (var page in section.Pages)
            {
                foreach (var row in page.Rows)
                {
                    var question = row.GroupLabel is { Length: > 0 } group ? $"{group} – {row.Question}" : row.Question;
                    yield return [.. prefix, section.Title, page.Title, question, row.Answer];
                }
            }
        }
    }

    // The column set for a window. Derived from the CATALOGUE, not from the snapshots, so every export of a window
    // has the same shape whichever organisations happened to submit. maxRoles is the widest funded-roles answer
    // among the submitters (0 => no role columns at all). sectionCodes narrows it to the window's sections (BLUM-8093)
    // so a section the admin left off is not a block of always-blank columns; null keeps the whole catalogue.
    public static IReadOnlyList<WideColumn> WideColumns(int maxRoles, IReadOnlyCollection<string>? sectionCodes = null)
    {
        var columns = new List<WideColumn>();
        var headers = new HashSet<string>(StringComparer.Ordinal);

        var questions = MaturityQuestionResolver.ResolveAll()
            .Where(q => sectionCodes is null || sectionCodes.Contains(q.Section, StringComparer.Ordinal));

        foreach (var question in questions)
        {
            if (question.IsRoles)
            {
                for (var index = 1; index <= maxRoles; index++)
                {
                    columns.Add(new WideColumn($"Role {index}: role", question.Code, index, "role"));
                    columns.Add(new WideColumn($"Role {index}: time", question.Code, index, "time"));
                }

                continue;
            }

            var header = question.GroupLabel is { Length: > 0 } group ? $"{group} – {question.Label}" : question.Label;

            // Two questions can share a label ("What services do they deliver?" under both VCSE pages) — a
            // duplicate header would make the pivoted file unusable, so the page title disambiguates.
            if (!headers.Add(header))
            {
                header = $"{question.PageTitle}: {header}";
                headers.Add(header);
            }

            columns.Add(new WideColumn(header, question.Code, null, null));
        }

        return columns;
    }

    public static IReadOnlyList<string> WideHeader(IReadOnlyList<string> prefix, IReadOnlyList<WideColumn> columns) =>
        [.. prefix, .. columns.Select(c => c.Header)];

    public static IReadOnlyList<string> WideRow(
        IReadOnlyList<string> prefix,
        MaturityAssessmentSnapshotDocument snapshot,
        IReadOnlyList<WideColumn> columns)
    {
        var rowsByCode = snapshot.Sections
            .SelectMany(s => s.Pages)
            .SelectMany(p => p.Rows)
            .GroupBy(r => r.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var cells = new List<string>(prefix);
        foreach (var column in columns)
        {
            if (!rowsByCode.TryGetValue(column.Code, out var row))
            {
                cells.Add(string.Empty);
                continue;
            }

            if (column.RoleIndex is { } roleIndex)
            {
                cells.Add(RoleCell(row.Value, roleIndex, column.RoleField!));
                continue;
            }

            cells.Add(row.Answer);
        }

        return cells;
    }

    // The widest funded-roles answer across a set of snapshots — sizes the role column pairs.
    public static int MaxRoles(IEnumerable<MaturityAssessmentSnapshotDocument> snapshots)
    {
        var max = 0;
        foreach (var snapshot in snapshots)
        {
            var rolesRow = snapshot.Sections.SelectMany(s => s.Pages).SelectMany(p => p.Rows)
                .FirstOrDefault(r => r.Code == "MA_FUNDED_ROLES");
            if (rolesRow is not null && MaturityRolesAnswer.TryParse(rolesRow.Value, out var entries))
            {
                max = Math.Max(max, entries.Count);
            }
        }

        return max;
    }

    private static string RoleCell(string? value, int roleIndex, string field)
    {
        if (!MaturityRolesAnswer.TryParse(value, out var entries) || entries.Count < roleIndex)
        {
            return string.Empty;
        }

        var entry = entries[roleIndex - 1];
        return field == "role"
            ? MaturityRolesAnswer.RoleLabel(entry, MaturityProtocol.FundedRoleOptions)
            : MaturityRolesAnswer.LabelFor(MaturityProtocol.FundedTimeOptions, entry.Time);
    }
}