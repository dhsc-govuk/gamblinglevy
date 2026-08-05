namespace Application.Common.ReportSnapshots;

// One organisation's contribution to an export: the identity cells prefixed onto each of its rows (empty for the
// single-organisation LA export, {Organisation, ONS code, E-code} for the cross-organisation one) and its frozen
// snapshot.
public record ReportSnapshotCsvSource(IReadOnlyList<string> Prefix, ReportSnapshotDocument Snapshot);

// The CSV ROW SHAPES shared by the LA's per-organisation report export (Api.Services.Implementation
// .QuarterlyReportExportService) and DHSC's cross-organisation window export (ExportSubmissionsCommand). Both used to
// carry their own copy, so a change to a row's columns had to be made twice or the two ZIPs would describe the same
// submission differently (BLUM-6100 review).
//
// Only the rows are shared, not the file assembly: the two exports lay their files out differently on purpose. The LA
// file keeps the funding table and its summary metrics together in one small file a human reads top to bottom; the
// cross-organisation export gives each grain its own rectangular file, because a mid-file sub-header silently
// misaligns every summary amount under the per-activity columns when the file is aggregated.
public static class ReportSnapshotCsv
{
    // The trailing columns of the funding summary block, after any organisation-identity prefix.
    public static readonly string[] FundingSummaryColumns = ["Summary", "Amount (£)"];

    // {prefix}, {Activity|Section}, Question, Answer — activity key results and strategic feedback share this shape.
    public static List<IReadOnlyList<string>> SectionRows(
        IReadOnlyList<ReportSnapshotCsvSource> sources,
        Func<ReportSnapshotDocument, IReadOnlyList<ReportSnapshotSection>> selectSections)
    {
        var rows = new List<IReadOnlyList<string>>();
        foreach (var source in sources)
        {
            foreach (var section in selectSections(source.Snapshot))
            {
                foreach (var row in section.Rows)
                {
                    rows.Add([.. source.Prefix, section.Title, row.Question, row.Answer]);
                }
            }
        }

        return rows;
    }

    // {prefix}, then the frozen funding table's own already-rendered cells. Sources without funding contribute nothing.
    public static List<IReadOnlyList<string>> FundingRows(IReadOnlyList<ReportSnapshotCsvSource> sources)
    {
        var rows = new List<IReadOnlyList<string>>();
        foreach (var source in sources)
        {
            foreach (var fundingRow in source.Snapshot.Funding?.Rows ?? [])
            {
                rows.Add([.. source.Prefix, .. fundingRow]);
            }
        }

        return rows;
    }

    // {prefix}, metric name, amount — the per-organisation totals below the funding table.
    public static List<IReadOnlyList<string>> FundingSummaryRows(IReadOnlyList<ReportSnapshotCsvSource> sources)
    {
        var rows = new List<IReadOnlyList<string>>();
        foreach (var source in sources)
        {
            foreach (var summary in source.Snapshot.Funding?.Summary ?? [])
            {
                rows.Add([.. source.Prefix, summary.Question, summary.Answer]);
            }
        }

        return rows;
    }
}