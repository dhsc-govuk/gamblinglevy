using Application.Common.Csv;
using Application.Submissions.Protocol;

namespace Application.Submissions.Offline;

// The offline quarterly report (BLUM-8310): one CSV per section, which an LA downloads, fills in a spreadsheet and
// uploads back. This file is the model both directions share. The template writer renders an OfflineSheet and the
// upload validator reads a file against the OfflineSheet rebuilt from the database at upload time, so the file
// offered and the file accepted cannot drift apart.
//
// Layout, identical in every section:
//   row 1  headings ("Q7. Who is contributing evidence?")
//   row 2  how to answer each column (required or not, the allowed answers, and the conditional helper text that
//          stands in for the web form's show/hide logic)
//   row 3  machine keys; the reference column's cell is the file's identity (OfflineIdentity)
//   row 4+ one row per activity (or the single strategic feedback row)
public enum OfflineColumnKind
{
    // Column A: an activity's reference on a data row, the file's identity in the key row.
    Reference,

    // Shown for information and ignored on upload (activity name, type, status, a custom key result's text).
    ReadOnly,

    // Blank in a template; the upload writes each row's problems here in the copy it gives back.
    Errors,

    Question,
}

public sealed record OfflineColumn(
    string Key,
    OfflineColumnKind Kind,
    string Heading,
    string Guidance,
    int Index,
    int? QuestionNumber)
{
    public string Letter => SpreadsheetColumn.Letter(Index);

    // How guidance and error messages refer to the column: "Q7 (column K)", or "column C" for one with no number.
    public string Name => QuestionNumber is { } number ? $"Q{number} (column {Letter})" : $"column {Letter}";

    public string ShortName => QuestionNumber is { } number ? $"Q{number}" : $"column {Letter}";
}

// One question column that applies to one row, with the question as that row asks it. For activity results the
// question is exactly what KeyResultQuestionResolver resolved for the activity; for funding it is a money amount
// whose Code is FundingSpendValidator's error key for that amount ("actual-{id}", "other-forecast-q3"), so every
// validator error maps back to a cell by code.
public sealed record OfflineBinding(string ColumnKey, ResolvedQuestion Question);

public sealed record OfflineRow(
    string Reference,

    // How a "missing from your file" error names the row.
    string Name,
    IReadOnlyDictionary<string, string> ReadOnlyCells,
    IReadOnlyList<OfflineBinding> Bindings,

    // The stored values the template shows, by question code (already filtered to what is visible).
    IReadOnlyDictionary<string, string?> InitialValues)
{
    public OfflineBinding? BindingFor(string columnKey) =>
        Bindings.FirstOrDefault(binding => binding.ColumnKey == columnKey);

    public OfflineBinding? BindingForCode(string code) =>
        Bindings.FirstOrDefault(binding => binding.Question.Code == code);
}

public sealed record OfflineSheet(
    string ModuleCode,
    Guid CollectionId,
    Guid OrganisationId,
    string Fingerprint,
    IReadOnlyList<OfflineColumn> Columns,
    IReadOnlyList<OfflineRow> Rows,

    // Strategic feedback: exactly one data row, not matched by reference.
    bool SingleRow)
{
    public OfflineColumn ReferenceColumn => Columns.Single(column => column.Kind == OfflineColumnKind.Reference);

    public OfflineColumn ErrorsColumn => Columns.Single(column => column.Kind == OfflineColumnKind.Errors);

    public OfflineColumn Column(string key) => Columns.Single(column => column.Key == key);

    public string Identity => OfflineIdentity.Format(ModuleCode, CollectionId, OrganisationId, Fingerprint);
}

// One problem found in an uploaded file, located the way the spreadsheet shows it. ColumnIndex is null for a
// problem with a whole row (an unknown reference, a missing activity).
public sealed record OfflineCellError(int Row, int? ColumnIndex, int? QuestionNumber, string Message)
{
    public string? ColumnLetter => ColumnIndex is { } index ? SpreadsheetColumn.Letter(index) : null;

    // The line the annotated file's Errors cell carries for this problem.
    public string Describe() => (QuestionNumber, ColumnLetter) switch
    {
        ({ } number, { } letter) => $"Q{number} (column {letter}): {Message}",
        (null, { } letter) => $"Column {letter}: {Message}",
        _ => Message,
    };
}