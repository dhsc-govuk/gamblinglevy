namespace Application.Common.ReportSnapshots;

// The finalised, rendered report content frozen at submit time and stored (as JSON) on the submission. Only the
// section content that reads LIVE activity/form state is captured here — the reference number, submitted-at, local
// authority, financial year and reporting period are all derived at export time from the immutable submission +
// collection, so the snapshot has no dependency on the reference allocated during submit.
//
// Lives in Application rather than Api so the LA's per-organisation export and the Application-layer cross-organisation
// export read ONE set of types: there is no second projection and no mapper to keep in step (BLUM-6100 review).
//
// Every collection member coalesces null to empty. A partial snapshot — a member absent from the stored JSON, or a
// document written before that member existed — deserialises with nulls, and every consumer would otherwise NRE. The
// invariant lives on the type so it holds for every reader, not just the one that remembered to coalesce.
public record ReportSnapshotDocument(
    IReadOnlyList<ReportSnapshotSection> ActivitySections,
    ReportSnapshotFunding? Funding,
    IReadOnlyList<ReportSnapshotSection> FeedbackSections)
{
    public IReadOnlyList<ReportSnapshotSection> ActivitySections { get; init; } = ActivitySections ?? [];

    public IReadOnlyList<ReportSnapshotSection> FeedbackSections { get; init; } = FeedbackSections ?? [];
}

// One titled group of question/answer rows (an activity, or a strategic-feedback page). Only questions that were
// VISIBLE on screen (per the conditional-visibility filter) are included, each with its presenter-rendered answer.
public record ReportSnapshotSection(string Title, IReadOnlyList<ReportSnapshotRow> Rows)
{
    public IReadOnlyList<ReportSnapshotRow> Rows { get; init; } = Rows ?? [];
}

// Code is the question's protocol code (BLUM-8329), frozen so a consumer can find a question without matching its
// label, which copy edits and flags change. Null on rows frozen before it existed and on the funding summary rows,
// which are metrics rather than questions.
public record ReportSnapshotRow(string Question, string Answer, string? Code = null);

// The "Record funding spent" table frozen as already-rendered string cells (money formatted, dynamic forecast-quarter
// columns resolved), so the live activity titles it draws on are frozen too.
public record ReportSnapshotFunding(
    IReadOnlyList<string> Header,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    IReadOnlyList<ReportSnapshotRow> Summary,
    ReportSnapshotFundingTotals? Totals = null)
{
    public IReadOnlyList<string> Header { get; init; } = Header ?? [];

    public IReadOnlyList<IReadOnlyList<string>> Rows { get; init; } = Rows ?? [];

    public IReadOnlyList<ReportSnapshotRow> Summary { get; init; } = Summary ?? [];
}

// The organisation-level totals rendered as columns 5-17 of funding-summary.csv (BLUM-7130 CSV review). Pre-rendered
// money strings, frozen at submit time like the rest of the funding block. Strongly typed rather than folded into
// the melted Summary list above — the quarter breakdown is a matrix, not a scalar metric, and the wide CSV pivot
// must not string-match labels out of a free-form list. Nullable on ReportSnapshotFunding: snapshots frozen before
// this field existed deserialise with Totals = null, and every reader must treat that as "render blank cells", not
// throw.
public record ReportSnapshotFundingTotals(
    string AnnualAllocation,
    string Q1TotalActual,
    string Q2Forecast,
    string Q2TotalActual,
    string Q3Forecast,
    string Q3TotalActual,
    string Q4Forecast,
    string Q4TotalActual,
    string TotalSpendToDate,
    string RemainingBudget,
    string TotalForecast,
    string ProjectedYearEnd,
    string ProjectedVariance,
    // BLUM-7608. Nullable and trailing for the same reason Totals itself is nullable on ReportSnapshotFunding: a
    // snapshot frozen before these existed deserialises with them null, and every reader renders a blank cell
    // rather than throwing.
    string? AnnualAllocationFinancialYear = null,
    string? NextYearAnnualAllocation = null,
    string? NextYearFinancialYear = null);