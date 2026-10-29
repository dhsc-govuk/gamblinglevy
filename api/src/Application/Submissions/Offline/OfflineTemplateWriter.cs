using Application.Common.Csv;

namespace Application.Submissions.Offline;

// Renders an OfflineSheet as the CSV an LA downloads. UTF-8 with a BOM so Excel shows "£" and curly quotes, and every
// cell through CsvWriter's formula guard, as for every other file the service hands out.
public static class OfflineTemplateWriter
{
    public static byte[] Write(OfflineSheet sheet)
    {
        var headings = sheet.Columns.Select(column => column.Heading).ToList();
        var guidance = sheet.Columns.Select(column => column.Guidance).ToList();
        var keys = sheet.Columns.Select(column => column.Kind == OfflineColumnKind.Reference ? sheet.Identity : column.Key).ToList();

        var rows = new List<IReadOnlyList<string>> { guidance, keys };
        rows.AddRange(sheet.Rows.Select(row => (IReadOnlyList<string>)RowCells(sheet, row)));

        return CsvWriter.Build(headings, rows, byteOrderMark: true);
    }

    // A row as the template shows it, in the sheet's own column order. The annotated file reuses this for a row the
    // upload found missing, so a person can fill it in where it now stands.
    public static IReadOnlyList<string> RowCells(OfflineSheet sheet, OfflineRow row) =>
        sheet.Columns.Select(column => Cell(column, row)).ToList();

    private static string Cell(OfflineColumn column, OfflineRow row)
    {
        switch (column.Kind)
        {
            case OfflineColumnKind.Reference:
                return row.Reference;
            case OfflineColumnKind.Errors:
                return string.Empty;
            case OfflineColumnKind.ReadOnly:
                return row.ReadOnlyCells.TryGetValue(column.Key, out var text) ? text : OfflineCellValues.NotApplicable;
        }

        var binding = row.BindingFor(column.Key);
        if (binding is null)
        {
            return OfflineCellValues.NotApplicable;
        }

        return row.InitialValues.TryGetValue(binding.Question.Code, out var stored)
            ? OfflineCellValues.Display(binding.Question, stored)
            : string.Empty;
    }
}