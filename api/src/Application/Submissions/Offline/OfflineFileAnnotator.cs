using Application.Common.Csv;

namespace Application.Submissions.Offline;

// The copy of an invalid upload handed back to the person who sent it, so they can go on working in their
// spreadsheet: their own rows, in their own order and columns, untouched except for the Errors column, which now
// lists each row's problems ("Q7 (column K): Select who is contributing evidence") and nothing else — errors from an
// earlier round are cleared. A report row the file was missing is added at the end, filled in as the template would
// have it, carrying its own error. Row numbers therefore match the list shown on the page.
public static class OfflineFileAnnotator
{
    public static byte[] Annotate(OfflineSheet sheet, UploadedOfflineFile file, OfflineValidationResult result)
    {
        var errorsIndex = file.ColumnIndexByKey[sheet.ErrorsColumn.Key];
        var width = Math.Max(file.Records.Max(record => record.Cells.Count), errorsIndex + 1);
        var messages = result.Errors
            .GroupBy(error => error.Row)
            .ToDictionary(group => group.Key, group => string.Join("\n", group.Select(error => error.Describe())));

        var lines = new List<IReadOnlyList<string>>();
        foreach (var record in file.Records)
        {
            var cells = Pad(record.Cells, width);
            if (result.DataRowNumbers.Contains(record.RowNumber) || messages.ContainsKey(record.RowNumber))
            {
                cells[errorsIndex] = messages.TryGetValue(record.RowNumber, out var text) ? text : string.Empty;
            }

            lines.Add(cells);
        }

        foreach (var (row, rowNumber) in result.AppendedRows)
        {
            var cells = Pad([], width);
            var template = OfflineTemplateWriter.RowCells(sheet, row);
            for (var index = 0; index < sheet.Columns.Count; index++)
            {
                var column = sheet.Columns[index];
                var position = column.Kind == OfflineColumnKind.Reference
                    ? file.ReferenceColumnIndex
                    : file.ColumnIndexByKey[column.Key];
                cells[position] = template[index];
            }

            cells[errorsIndex] = messages.TryGetValue(rowNumber, out var text) ? text : string.Empty;
            lines.Add(cells);
        }

        return CsvWriter.Build(lines[0], lines.Skip(1), byteOrderMark: true);
    }

    // The annotated file's name: the uploaded file's own, marked, so it sits next to it in a downloads folder.
    public static string FileName(string? uploadedFileName, string fallback)
    {
        var stem = Path.GetFileNameWithoutExtension(uploadedFileName ?? string.Empty);
        var safe = new string(stem.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ').ToArray()).Trim();
        if (safe.EndsWith("-with-errors", StringComparison.OrdinalIgnoreCase))
        {
            safe = safe[..^"-with-errors".Length];
        }

        return $"{(safe.Length == 0 ? fallback : safe)}-with-errors.csv";
    }

    private static string[] Pad(IReadOnlyList<string> cells, int width)
    {
        var padded = new string[Math.Max(width, cells.Count)];
        for (var index = 0; index < padded.Length; index++)
        {
            padded[index] = index < cells.Count ? cells[index] : string.Empty;
        }

        return padded;
    }
}