using Application.Common.Csv;
using Application.Forms.Answers;
using Application.Submissions.Protocol;

namespace Application.Submissions.Offline;

// One answer row of an upload, matched to the report row it is for, with each cell turned into the value to store.
public sealed record OfflineRowAnswers(OfflineRow Row, int RowNumber, IReadOnlyDictionary<string, string?> Answers);

// A section validator's verdict on one answer, located by the question code the section's own validator uses.
public sealed record OfflineCodeError(OfflineRow Row, string Code, string Message);

public sealed record OfflineValidationResult(
    IReadOnlyList<OfflineCellError> Errors,
    IReadOnlyList<OfflineRowAnswers> Rows,

    // Report rows the file did not have, each with the spreadsheet row the annotated file adds it at.
    IReadOnlyList<(OfflineRow Row, int RowNumber)> AppendedRows,

    // The answer rows of the file (not the heading, guidance or key rows), whose Errors cells the annotated file
    // rewrites.
    IReadOnlySet<int> DataRowNumbers)
{
    public bool IsValid => Errors.Count == 0;
}

// Checks an uploaded offline file against the sheet rebuilt from the database, the way the web form checks a page:
// every cell is read into the value it would store, then the section's own validator runs with required answers
// enforced (a valid file completes the section). Its errors come back keyed by question code and are mapped to the
// cell that holds the answer. On top of the web's rules there are two a spreadsheet needs: a cell whose question
// the row is not asked must stay N/A, and a follow-up answered while its condition is not met is an error — the web
// form hides such a question and drops its answer, but a spreadsheet cannot hide it, so dropping it silently would
// lose something the person meant to give us.
public static class OfflineUploadValidator
{
    public const string NotApplicableMessage = "This question does not apply to this activity. Change it back to N/A";

    // The layout check behind OFFLINE_COLUMNS_CHANGED: every column the sheet has is in the file exactly once, and
    // the file adds none of its own. A column with a blank key is someone's working-out and is ignored.
    public static bool HasExpectedColumns(OfflineSheet sheet, UploadedOfflineFile file)
    {
        if (file.DuplicateKeys.Count > 0)
        {
            return false;
        }

        var expected = sheet.Columns
            .Where(column => column.Kind != OfflineColumnKind.Reference)
            .Select(column => column.Key)
            .ToHashSet(StringComparer.Ordinal);

        return expected.All(file.ColumnIndexByKey.ContainsKey) && file.ColumnIndexByKey.Keys.All(expected.Contains);
    }

    // transitive is BLUM_9087: on, a follow-up whose parent question is itself hidden counts as hidden too.
    public static OfflineValidationResult Validate(
        OfflineSheet sheet,
        UploadedOfflineFile file,
        Func<IReadOnlyList<OfflineRowAnswers>, IReadOnlyList<OfflineCodeError>> sectionRules,
        bool transitive)
    {
        var errors = new List<OfflineCellError>();
        var referenceLetter = SpreadsheetColumn.Letter(file.ReferenceColumnIndex);
        var dataRecords = DataRecords(sheet, file);
        var matched = new List<(OfflineRow Row, CsvRecord Record)>();

        if (sheet.SingleRow)
        {
            if (dataRecords.Count > 0)
            {
                matched.Add((sheet.Rows[0], dataRecords[0]));
            }

            errors.AddRange(dataRecords.Skip(1).Select(record =>
                new OfflineCellError(record.RowNumber, null, null, "Only use one row for your answers. Delete this row")));
        }
        else
        {
            var rowsByReference = sheet.Rows.ToDictionary(row => ReferenceKey(row.Reference), StringComparer.Ordinal);
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var record in dataRecords)
            {
                var reference = OfflineCellValues.StripFormulaGuard(record.CellAt(file.ReferenceColumnIndex).Trim());
                if (reference.Length == 0)
                {
                    errors.Add(new OfflineCellError(
                        record.RowNumber,
                        file.ReferenceColumnIndex,
                        null,
                        $"Column {referenceLetter} is blank. Copy this activity’s reference from a new spreadsheet, or delete this row"));
                    continue;
                }

                var key = ReferenceKey(reference);
                if (!rowsByReference.TryGetValue(key, out var row))
                {
                    errors.Add(new OfflineCellError(
                        record.RowNumber,
                        file.ReferenceColumnIndex,
                        null,
                        $"The reference in column {referenceLetter} does not match an activity in this report. Do not change column {referenceLetter}"));
                    continue;
                }

                if (seen.TryGetValue(key, out var firstRow))
                {
                    errors.Add(new OfflineCellError(
                        record.RowNumber,
                        file.ReferenceColumnIndex,
                        null,
                        $"This activity is also in row {firstRow}. Delete one of the rows"));
                    continue;
                }

                seen[key] = record.RowNumber;
                matched.Add((row, record));
            }
        }

        var answers = new List<OfflineRowAnswers>();
        var failedCodes = new Dictionary<OfflineRow, HashSet<string>>();
        foreach (var (row, record) in matched)
        {
            var (rowAnswers, rowErrors, failed) = ReadRow(sheet, file, row, record, transitive);
            errors.AddRange(rowErrors);
            failedCodes[row] = failed;
            answers.Add(new OfflineRowAnswers(row, record.RowNumber, rowAnswers));
        }

        var rowNumberByRow = answers.ToDictionary(answer => answer.Row, answer => answer.RowNumber);
        foreach (var error in sectionRules(answers))
        {
            if (!rowNumberByRow.TryGetValue(error.Row, out var rowNumber) || failedCodes[error.Row].Contains(error.Code))
            {
                // A row that is not in the file is reported as missing instead; an answer that could not be read has
                // already been reported as such.
                continue;
            }

            var binding = error.Row.BindingForCode(error.Code);
            errors.Add(binding is null
                ? new OfflineCellError(rowNumber, null, null, Tidy(error.Message))
                : CellError(sheet, file, rowNumber, binding.ColumnKey, error.Message));
        }

        var present = matched.Select(match => match.Row).ToHashSet();
        var appended = new List<(OfflineRow Row, int RowNumber)>();
        var nextRow = file.Records.Count + 1;
        foreach (var row in sheet.Rows.Where(row => !present.Contains(row)))
        {
            appended.Add((row, nextRow));
            errors.Add(new OfflineCellError(
                nextRow,
                null,
                null,
                sheet.SingleRow
                    ? "Your answers are missing from your file. Answer the questions in this row"
                    : $"{row.Name} is missing from your file. Answer its questions in this row"));
            nextRow++;
        }

        var ordered = errors
            .OrderBy(error => error.Row)
            .ThenBy(error => error.ColumnIndex ?? -1)
            .ToList();

        return new OfflineValidationResult(ordered, answers, appended, dataRecords.Select(record => record.RowNumber).ToHashSet());
    }

    // The answer rows: everything but the heading, guidance and key rows, wherever a sort has put them, and rows
    // with nothing in them.
    private static IReadOnlyList<CsvRecord> DataRecords(OfflineSheet sheet, UploadedOfflineFile file)
    {
        var referenceHeading = sheet.ReferenceColumn.Heading;
        var errorsIndex = file.ColumnIndexByKey.TryGetValue(sheet.ErrorsColumn.Key, out var index) ? index : -1;

        return file.Records
            .Where(record => record != file.KeyRow)
            .Where(record =>
            {
                var reference = record.CellAt(file.ReferenceColumnIndex).Trim();
                var errorsCell = record.CellAt(errorsIndex).Trim();
                var isHeading = reference.Equals(referenceHeading, StringComparison.OrdinalIgnoreCase)
                    || errorsCell.Equals(sheet.ErrorsColumn.Heading, StringComparison.OrdinalIgnoreCase);
                var isGuidance = reference.StartsWith("How to answer", StringComparison.OrdinalIgnoreCase)
                    || errorsCell.Equals(sheet.ErrorsColumn.Guidance, StringComparison.OrdinalIgnoreCase);
                return !isHeading && !isGuidance;
            })
            .Where(record => record.Cells.Where((_, cellIndex) => cellIndex != errorsIndex).Any(cell => !string.IsNullOrWhiteSpace(cell)))
            .ToList();
    }

    private static (IReadOnlyDictionary<string, string?> Answers, IReadOnlyList<OfflineCellError> Errors, HashSet<string> Failed) ReadRow(
        OfflineSheet sheet,
        UploadedOfflineFile file,
        OfflineRow row,
        CsvRecord record,
        bool transitive)
    {
        var answers = new Dictionary<string, string?>(StringComparer.Ordinal);
        var errors = new List<OfflineCellError>();
        var failed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var column in sheet.Columns.Where(column => column.Kind == OfflineColumnKind.Question))
        {
            var cell = OfflineCellValues.StripFormulaGuard(record.CellAt(file.ColumnIndexByKey[column.Key]).Trim());
            var binding = row.BindingFor(column.Key);

            if (binding is null)
            {
                if (!OfflineCellValues.IsBlankOrNotApplicable(cell))
                {
                    errors.Add(CellError(sheet, file, record.RowNumber, column.Key, NotApplicableMessage));
                }

                continue;
            }

            if (OfflineCellValues.IsBlankOrNotApplicable(cell))
            {
                continue;
            }

            var parsed = OfflineCellValues.Parse(binding.Question, cell);
            if (parsed.IsError)
            {
                failed.Add(binding.Question.Code);
                errors.Add(CellError(sheet, file, record.RowNumber, column.Key, parsed.Error!));
                continue;
            }

            if (parsed.Value is not null)
            {
                answers[binding.Question.Code] = parsed.Value;
            }
        }

        var visibility = SubmissionAnswerValidator.Visibility(row.Bindings.Select(binding => binding.Question), answers, transitive);
        var questionsByCode = row.Bindings
            .GroupBy(binding => binding.Question.Code, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Question, StringComparer.Ordinal);

        foreach (var binding in row.Bindings.Where(binding => answers.ContainsKey(binding.Question.Code)))
        {
            var parentCode = binding.Question.ConditionalQuestionCode;
            if (string.IsNullOrWhiteSpace(parentCode)
                || failed.Contains(parentCode)
                || visibility.IsVisible(binding.Question.Code)
                || (transitive && IsAccountedForAbove(parentCode, questionsByCode, answers, visibility, failed)))
            {
                continue;
            }

            failed.Add(binding.Question.Code);
            errors.Add(CellError(sheet, file, record.RowNumber, binding.ColumnKey, HiddenAnswerMessage(sheet, row, binding)));
        }

        return (answers, errors, failed);
    }

    // With BLUM_9087 a follow-up is hidden whenever anything above it is, so one cause would otherwise be reported all
    // the way down the chain, each message blaming a parent the file may have answered correctly. Only the topmost cause
    // is reported: an answer is passed over when a question above it failed to read, or is itself answered while hidden
    // (that one gets the error). Either way the parent's error says what to fix.
    private static bool IsAccountedForAbove(
        string? code,
        IReadOnlyDictionary<string, ResolvedQuestion> questionsByCode,
        IReadOnlyDictionary<string, string?> answers,
        AnswerVisibility visibility,
        IReadOnlySet<string> failed)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (!string.IsNullOrWhiteSpace(code) && seen.Add(code))
        {
            var answered = answers.TryGetValue(code, out var value) && !string.IsNullOrWhiteSpace(value);
            if (failed.Contains(code) || (answered && !visibility.IsVisible(code)))
            {
                return true;
            }

            code = questionsByCode.TryGetValue(code, out var question) ? question.ConditionalQuestionCode : null;
        }

        return false;
    }

    private static string HiddenAnswerMessage(OfflineSheet sheet, OfflineRow row, OfflineBinding followUp)
    {
        var parent = row.BindingForCode(followUp.Question.ConditionalQuestionCode!);
        var child = sheet.Column(followUp.ColumnKey).ShortName;
        if (parent is null)
        {
            return $"Clear {child}. It only needs an answer when the question it follows on from asks for one";
        }

        var parentName = sheet.Column(parent.ColumnKey).ShortName;
        var triggers = OfflineGuidanceText.TriggerLabels(followUp.Question, parent.Question);
        var chose = OfflineGuidanceText.ChoseVerb(parent.Question);
        var preposition = OfflineGuidanceText.ChosePreposition(parent.Question);
        var choose = chose == "answered" ? "Answer" : "Choose";

        return $"You answered {child} but did not {(chose == "answered" ? "answer" : "choose")} {triggers} {preposition} {parentName}. "
            + $"{choose} {triggers} {preposition} {parentName} or clear {child}";
    }

    private static OfflineCellError CellError(OfflineSheet sheet, UploadedOfflineFile file, int rowNumber, string columnKey, string message) =>
        new(rowNumber, file.ColumnIndexByKey[columnKey], sheet.Column(columnKey).QuestionNumber, Tidy(message));

    // Messages are listed as GDS error messages, which do not end in a full stop; some validator messages do.
    private static string Tidy(string message) => message.Trim().TrimEnd('.');

    private static string ReferenceKey(string reference) =>
        Guid.TryParse(reference, out var id) ? id.ToString("N") : reference.Trim().ToUpperInvariant();
}