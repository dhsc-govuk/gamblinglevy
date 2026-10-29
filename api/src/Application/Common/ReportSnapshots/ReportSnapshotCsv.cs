namespace Application.Common.ReportSnapshots;

// One organisation's contribution to an export: the identity cells prefixed onto each of its rows and its frozen
// snapshot. The single-organisation LA export passes {Organisation}, the cross-organisation one
// {Organisation, Region, ONS code, E-code} (pinned by DhscSubmissionExportTests
// .ExportSubmissions_SectionFiles_CarrySubmitterSnapshotDataWithOrgColumns). The LA export carried an EMPTY prefix
// until BLUM-7647 — a downloaded file that did not say whose report it was.
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
    // funding-spend.csv's header (BLUM-7130 CSV review) — fixed (Q1-4 actual, Q2-4 forecast) regardless of which
    // quarter is being reported. The single source of truth for what a CURRENT-shape frozen row looks like: both
    // ReportSnapshotService.BuildFunding (writes it at submit time) and ExportSubmissionsCommand (reads it back,
    // and filters out any source whose own frozen header doesn't match this — a window can straddle a deploy that
    // changed this shape) reference this one constant rather than keeping their own hand-synced copies.
    public static readonly string[] FundingHeader =
    [
        "Activity",
        "Category",
        "Activity ID",
        "Q1 total actual (£)",
        "Q2 Forecast (£)",
        "Q2 total actual (£)",
        "Q3 Forecast (£)",
        "Q3 total actual (£)",
        "Q4 Forecast (£)",
        "Q4 total actual (£)",
        "Total actual to date (£)",
        "Projected activity spend (£)"
    ];

    // The label written into the ACTIVITY cell of a per-organisation subtotal row (BLUM-7668) — what a human reading
    // the Activity column down the page sees. It is not the machine-readable marker; RowTypeColumn below is.
    public const string SubtotalLabel = "Subtotal";

    // funding-spend.csv carries two grains — one row per activity, then one subtotal per organisation (BLUM-7668) —
    // and this column is the only thing that lets a consumer tell them apart. Without it the sole marker is the
    // literal "Subtotal" in the Activity column, which is free text an LA can legitimately type as an activity name
    // (BLUM-7620 made the name mandatory, not reserved), so a whole-column SUM over the file silently double-counts
    // every organisation's money — the exact defect BLUM-6100/BLUM-7130 made this file one rectangle to prevent. An
    // analyst pivots on Row type = Activity and the sum is right again.
    //
    // APPENDED LAST, never inserted: every column that existed before keeps its position, so anything reading this
    // file by index rather than by header name is unaffected.
    public const string RowTypeColumn = "Row type";

    public const string ActivityRowType = "Activity";

    public const string SubtotalRowType = "Subtotal";

    // The Activity-cell label and Row type of the single row that closes the DHSC cross-organisation funding-spend.csv
    // (BLUM-9021, behind that flag): every money column totalled across every organisation in the file. A third grain,
    // kept apart from the other two by RowTypeColumn exactly as the subtotal is — a consumer that keeps only
    // Row type = Activity before summing still gets the right answer.
    public const string GrandTotalLabel = "Grand total";

    public const string GrandTotalRowType = "Grand total";

    // Every money column of every shape this file has ever had is named "<something> (£)", and no other column is.
    // That suffix is what tells a subtotal which columns to add up without hardcoding indices into a header that
    // has already changed twice.
    private const string MoneyColumnSuffix = "(£)";

    // The trailing columns of the melted funding summary sub-header, after any organisation-identity prefix — still
    // used as-is by the LA's own export (QuarterlyReportExportService), which keeps the funding table and its
    // summary metrics together in one small file read top to bottom.
    public static readonly string[] FundingSummaryColumns = ["Summary", "Amount (£)"];

    // The trailing columns of funding-summary.csv, after any organisation-identity prefix and the financial year —
    // one WIDE row per organisation (BLUM-7130 CSV review), for the DHSC cross-organisation export only. Distinct
    // from FundingSummaryColumns above (a different consumer, a different shape) rather than replacing it.
    public static readonly string[] FundingSummaryTotalsColumns =
    [
        "Annual allocation (£)",
        "Annual allocation financial year",
        "Next year annual allocation (£)",
        "Next year financial year",
        "Q1 total actual (£)",
        "Q2 Forecast (£)",
        "Q2 total actual (£)",
        "Q3 Forecast (£)",
        "Q3 total actual (£)",
        "Q4 Forecast (£)",
        "Q4 total actual (£)",
        "Total spend to date (£)",
        "Remaining allocation (£)",
        "Total Forecast (£)",
        "Forecasted year-end (£)",
        "Forecasted variance (£)"
    ];

    // strategic-feedback.csv's questions behind BLUM_8329 — one row per organisation, one column per question, in the
    // order the report asks them — each by the code SeedStrategicFeedbackModuleAsync gives it and the header its
    // column carries. The header is the current product copy without the " (optional)" suffix BLUM_7637 adds to the
    // first two: it names the question, and must not change with the flag.
    private static readonly (string Code, string Header)[] StrategicFeedbackQuestions =
    [
        ("SF_OTHER_PREVENTION", "What other gambling harms prevention work have you focused on this quarter?"),
        ("SF_CHALLENGES", "What challenges have you encountered?"),
        ("SF_DHSC_SUPPORT", "Do you have any feedback for the Department of Health and Social Care (DHSC) on gambling harms prevention?"),
        ("SF_LEARNINGS", "Do you have any learnings you’d like to share with other local authorities?"),
    ];

    public static readonly string[] StrategicFeedbackQuestionColumns = [.. StrategicFeedbackQuestions.Select(q => q.Header)];

    // A snapshot frozen before its rows carried a question code names each question only by the label it had at
    // submit time, so those reports find their column through every label the question has been frozen under:
    // BLUM-6613 shipped the first set, and the Figma alignment (7207212) reworded three of them. The set is closed —
    // every snapshot frozen since BLUM-8329 carries the code — so a future rewording needs nothing here.
    private static readonly (string Code, string Label)[] LegacyStrategicFeedbackLabels =
    [
        ("SF_OTHER_PREVENTION", "What other gambling harms prevention work have you focused on this quarter?"),
        ("SF_OTHER_PREVENTION", "What other prevention work have you focused on this quarter?"),
        ("SF_CHALLENGES", "What challenges have you encountered?"),
        ("SF_DHSC_SUPPORT", "Do you have any feedback for the Department of Health and Social Care (DHSC) on gambling harms prevention?"),
        ("SF_DHSC_SUPPORT", "How could the Department of Health and Social Care (DHSC) better support your work?"),
        ("SF_LEARNINGS", "Do you have any learnings you’d like to share with other local authorities?"),
        ("SF_LEARNINGS", "What learnings would you like to share with other local authorities?"),
    ];

    private static readonly Dictionary<string, string> LegacyStrategicFeedbackCodeByLabel = BuildLegacyStrategicFeedbackLookup();

    private const string OptionalLabelSuffix = " (optional)";

    // The four answer cells of one organisation's strategic feedback, in StrategicFeedbackQuestionColumns order, or
    // null when the snapshot holds no strategic feedback — that organisation gets no row, exactly as it contributes
    // none to the long file. A question left blank, or one that is not one of the four, is an empty cell.
    //
    // Matched by question rather than by position because the long file never guaranteed four rows in this order —
    // a hidden or added question would slide every later answer under the wrong heading.
    public static IReadOnlyList<string>? StrategicFeedbackAnswers(ReportSnapshotDocument snapshot)
    {
        if (snapshot.FeedbackSections.Count == 0)
        {
            return null;
        }

        var answers = new string?[StrategicFeedbackQuestions.Length];
        foreach (var row in snapshot.FeedbackSections.SelectMany(section => section.Rows))
        {
            var column = StrategicFeedbackColumn(row);
            if (column >= 0)
            {
                answers[column] ??= row.Answer;
            }
        }

        return answers.Select(answer => answer ?? string.Empty).ToArray();
    }

    // The row's column, or -1: by the question code frozen with the row, which no copy edit or flag can change, and
    // only for a row frozen before codes were (Code is null) by its label.
    private static int StrategicFeedbackColumn(ReportSnapshotRow row)
    {
        var code = row.Code
            ?? (LegacyStrategicFeedbackCodeByLabel.TryGetValue(NormaliseFeedbackLabel(row.Question), out var legacyCode) ? legacyCode : null);
        return Array.FindIndex(StrategicFeedbackQuestions, question => string.Equals(question.Code, code, StringComparison.Ordinal));
    }

    // TryAdd, not ToDictionary: this runs in the type initialiser of a class every export and the submit-time snapshot
    // build depend on, so a duplicate label must cost the lookup its second copy rather than throw and take all of
    // them down until a redeploy.
    private static Dictionary<string, string> BuildLegacyStrategicFeedbackLookup()
    {
        var codeByLabel = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (code, label) in LegacyStrategicFeedbackLabels)
        {
            codeByLabel.TryAdd(NormaliseFeedbackLabel(label), code);
        }

        return codeByLabel;
    }

    // Drops BLUM_7637's " (optional)" suffix, which comes and goes with the flag, and folds the typographic
    // apostrophe the seeded copy uses into the one a keyboard types, so neither decides which column an answer is in.
    // Null-tolerant because a row read back from stored JSON is not guaranteed a label, and one bad row must not
    // fail a window export covering every organisation.
    private static string NormaliseFeedbackLabel(string? label)
    {
        var normalised = (label ?? string.Empty).Trim().Replace('’', '\'');
        return normalised.EndsWith(OptionalLabelSuffix, StringComparison.OrdinalIgnoreCase)
            ? normalised[..^OptionalLabelSuffix.Length].TrimEnd()
            : normalised;
    }

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

    // {prefix}, [financial year], then the frozen funding table's own already-rendered cells. Sources without
    // funding contribute nothing. financialYear is a single value shared by every organisation in one DHSC export
    // window, not part of the frozen per-organisation snapshot, so it is threaded in here rather than stored on the
    // snapshot — optional because the LA's own single-organisation export (one financial year, already obvious from
    // context) does not want the extra column, and its funding.Header has no matching cell for it.
    //
    // Each organisation's block is closed by a subtotal row totalling every money column across that organisation's
    // OWN rows (BLUM-7668), so a reader can see one LA's totals without selecting its rows and summing them by hand.
    // Every row — data and subtotal alike — ends with the RowTypeColumn marker, so the two grains stay machine-
    // separable; the caller is responsible for naming that column in the header it passes to CsvWriter.
    public static List<IReadOnlyList<string>> FundingRows(IReadOnlyList<ReportSnapshotCsvSource> sources, string? financialYear = null)
    {
        var rows = new List<IReadOnlyList<string>>();
        foreach (var source in sources)
        {
            var funding = source.Snapshot.Funding;
            var fundingRows = funding?.Rows ?? [];
            foreach (var fundingRow in fundingRows)
            {
                rows.Add(Prefixed(source.Prefix, financialYear, fundingRow, ActivityRowType));
            }

            // No rows means no block to total: an organisation with no funding section contributed nothing to this
            // file before and must keep contributing nothing, or the export gains a row for an organisation that
            // recorded no funding at all. An empty header has no columns to total against either — the LA's own
            // export renders funding.Header AS the CSV header, so that document is already unusable and a subtotal
            // of unknown width would only widen the damage.
            if (funding is not null && fundingRows.Count > 0 && funding.Header.Count > 0)
            {
                rows.Add(Prefixed(source.Prefix, financialYear, Total(SubtotalLabel, funding.Header, fundingRows), SubtotalRowType));
            }
        }

        return rows;
    }

    // The row that closes the DHSC cross-organisation funding-spend.csv (BLUM-9021): {prefix}, [financial year], then
    // every money column totalled across EVERY funding row of EVERY source — the same rows, read by the same parser,
    // that FundingRows subtotals per organisation, so it equals the sum of the subtotals by construction rather than
    // by a second derivation that could drift from them (the BLUM-7607 lesson). It adds up the activity rows, not the
    // subtotal rows: a subtotal is an exact decimal sum of those same cells, so the answer is identical and there is
    // one fewer parse of a rendered cell between the money and the total.
    //
    // Totalled against the CURRENT FundingHeader, and only over sources frozen in that shape — enforced here rather
    // than left to the caller. One total spans every organisation, so it has no single frozen header to read its money
    // columns off (unlike a subtotal); a window can straddle a deploy that changed the shape, and an older-shape row
    // read by today's indices would silently add its money into the wrong columns. The DHSC export already drops
    // those sources from the file, so for it this is a no-op; it is what keeps the method safe for any other caller.
    // Null when no current-shape source contributed a funding row — an export with nothing in it keeps the
    // header-only file it has always been rather than gaining an all-blank total. The prefix is the caller's, because
    // what belongs in the organisation-identity cells of a row that is no single organisation is the export's
    // decision, not this row shape's.
    public static IReadOnlyList<string>? FundingGrandTotalRow(
        IReadOnlyList<ReportSnapshotCsvSource> sources,
        IReadOnlyList<string> prefix,
        string? financialYear = null)
    {
        var fundingRows = sources
            .Select(source => source.Snapshot.Funding)
            .Where(funding => funding is not null && funding.Header.SequenceEqual(FundingHeader))
            .SelectMany(funding => funding!.Rows)
            .ToList();
        return fundingRows.Count == 0
            ? null
            : Prefixed(prefix, financialYear, Total(GrandTotalLabel, FundingHeader, fundingRows), GrandTotalRowType);
    }

    // {prefix}, [financial year], the frozen table's own cells, then the row-type marker that closes every row.
    private static IReadOnlyList<string> Prefixed(
        IReadOnlyList<string> prefix,
        string? financialYear,
        IReadOnlyList<string> cells,
        string rowType) =>
        financialYear is null
            ? [.. prefix, .. cells, rowType]
            : [.. prefix, financialYear, .. cells, rowType];

    // One row the width of the frozen header: the label in the first cell, a total in every money column, blank
    // everywhere else. A subtotal passes one organisation's rows, the grand total every organisation's. Either way it
    // totals EVERY row passed — reportable activities, the "Other" row and the "(no longer reported)" orphans alike —
    // which is the set that makes a subtotal agree with funding-summary.csv's organisation totals rather than
    // contradict them (BLUM-7607).
    //
    // Which columns hold money is read off the SOURCE'S OWN header rather than a fixed set of indices. The LA's
    // per-organisation export renders whatever shape was frozen (it has no compatibility filter, unlike the
    // cross-organisation one), so a snapshot predating a header change still totals its own columns instead of the
    // current ones. Every money column this file has ever had ends in "(£)"; nothing else does.
    //
    // A column where none of the rows carries a parseable value stays BLANK rather than becoming 0.00 — blank is
    // "nothing was recorded", which is what a future quarter's column is, and 0.00 is an answer somebody actually
    // gave. See ReportSnapshotMoney.Format.
    private static IReadOnlyList<string> Total(string label, IReadOnlyList<string> header, IReadOnlyList<IReadOnlyList<string>> fundingRows)
    {
        var cells = new string[header.Count];
        cells[0] = label;

        for (var column = 1; column < header.Count; column++)
        {
            if (!header[column].EndsWith(MoneyColumnSuffix, StringComparison.Ordinal))
            {
                cells[column] = string.Empty;
                continue;
            }

            decimal? total = null;
            foreach (var fundingRow in fundingRows)
            {
                // A row narrower than the header is not supposed to exist, but Rows and Header are untyped string
                // lists with no schema version, so a ragged legacy document skips the cell rather than throwing.
                if (column < fundingRow.Count && ReportSnapshotMoney.TryParse(fundingRow[column], out var value))
                {
                    total = (total ?? 0m) + value;
                }
            }

            cells[column] = ReportSnapshotMoney.Format(total);
        }

        return cells;
    }

    // {prefix}, metric name, amount — the per-organisation totals below the funding table in the LA's own export
    // (generic melted display, unaffected by the wide pivot below).
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

    // {prefix}, financial year, then the organisation-level totals — one wide row per organisation for
    // funding-summary.csv (BLUM-7130 CSV review). An organisation with no funding section, or a snapshot frozen
    // before ReportSnapshotFundingTotals existed, contributes blank cells rather than being omitted, so the file
    // stays one rectangle across every organisation in the export.
    public static List<IReadOnlyList<string>> FundingSummaryTotalsRows(IReadOnlyList<ReportSnapshotCsvSource> sources, string financialYear)
    {
        var rows = new List<IReadOnlyList<string>>();
        foreach (var source in sources)
        {
            var totals = source.Snapshot.Funding?.Totals;
            rows.Add(
            [
                .. source.Prefix,
                financialYear,
                totals?.AnnualAllocation ?? string.Empty,
                totals?.AnnualAllocationFinancialYear ?? string.Empty,
                totals?.NextYearAnnualAllocation ?? string.Empty,
                totals?.NextYearFinancialYear ?? string.Empty,
                totals?.Q1TotalActual ?? string.Empty,
                totals?.Q2Forecast ?? string.Empty,
                totals?.Q2TotalActual ?? string.Empty,
                totals?.Q3Forecast ?? string.Empty,
                totals?.Q3TotalActual ?? string.Empty,
                totals?.Q4Forecast ?? string.Empty,
                totals?.Q4TotalActual ?? string.Empty,
                totals?.TotalSpendToDate ?? string.Empty,
                totals?.RemainingBudget ?? string.Empty,
                totals?.TotalForecast ?? string.Empty,
                totals?.ProjectedYearEnd ?? string.Empty,
                totals?.ProjectedVariance ?? string.Empty
            ]);
        }

        return rows;
    }
}