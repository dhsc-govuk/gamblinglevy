namespace Application.MaturityAssessments.Snapshots;

// The finalised, rendered maturity assessment frozen at submit time and stored (as JSON) on the assessment. Only
// VISIBLE questions of VISIBLE pages are captured, each with its presenter-rendered answer and — because the
// exports need to flatten choice and repeater answers differently — the raw stored value alongside. The reference
// number, submitted-at, organisation and financial year are derived at read time from the immutable assessment +
// collection, so the snapshot carries none of them.
//
// Lives in Application rather than Api so the DHSC consolidated export command and the Api read side share one
// set of types. Every collection member coalesces null to empty, so a partial document never NREs a reader.
public record MaturityAssessmentSnapshotDocument(IReadOnlyList<MaturitySnapshotSection> Sections)
{
    public IReadOnlyList<MaturitySnapshotSection> Sections { get; init; } = Sections ?? [];
}

public record MaturitySnapshotSection(string Code, string Title, IReadOnlyList<MaturitySnapshotPage> Pages)
{
    public IReadOnlyList<MaturitySnapshotPage> Pages { get; init; } = Pages ?? [];
}

public record MaturitySnapshotPage(string Code, string Title, IReadOnlyList<MaturitySnapshotRow> Rows)
{
    public IReadOnlyList<MaturitySnapshotRow> Rows { get; init; } = Rows ?? [];
}

// Question is the label the LA saw; Answer is the rendered display (option labels, joined checkbox labels, the
// "Role 1: …" roles summary); Value is the raw stored answer (option values, comma-joined, JSON for roles) so the
// wide CSV and the read-only views can re-derive per-item cells; GroupLabel names the matrix a row belongs to.
public record MaturitySnapshotRow(string Code, string Question, string Answer, string? Value = null, string? GroupLabel = null);