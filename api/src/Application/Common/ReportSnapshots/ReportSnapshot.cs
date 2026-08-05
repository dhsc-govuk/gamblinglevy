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

public record ReportSnapshotRow(string Question, string Answer);

// The "Record funding spent" table frozen as already-rendered string cells (money formatted, dynamic forecast-quarter
// columns resolved), so the live activity titles it draws on are frozen too.
public record ReportSnapshotFunding(
    IReadOnlyList<string> Header,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    IReadOnlyList<ReportSnapshotRow> Summary)
{
    public IReadOnlyList<string> Header { get; init; } = Header ?? [];

    public IReadOnlyList<IReadOnlyList<string>> Rows { get; init; } = Rows ?? [];

    public IReadOnlyList<ReportSnapshotRow> Summary { get; init; } = Summary ?? [];
}