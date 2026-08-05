using System.Text;

namespace Application.Common.Csv;

// Builds CSV byte content (UTF-8, no BOM) with RFC-4180 quoting plus spreadsheet formula-injection neutralisation.
// Centralises the escaping rules so every export quotes and neutralises cells the same way.
public static class CsvWriter
{
    public static byte[] Build(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows)
    {
        var lines = new List<string> { JoinRow(header) };
        lines.AddRange(rows.Select(JoinRow));
        var csv = string.Join("\r\n", lines);
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(csv);
    }

    private static string JoinRow(IReadOnlyList<string> fields) => string.Join(",", fields.Select(Escape));

    public static string Escape(string? field)
    {
        field ??= string.Empty;

        // Neutralise spreadsheet formula injection: a cell beginning with one of these is evaluated as a live formula
        // by Excel/Sheets, so prefix a single quote to force the cell to be read as text.
        if (field.Length > 0 && field[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            field = "'" + field;
        }

        if (field.IndexOfAny(['"', ',', '\n', '\r']) < 0)
        {
            return field;
        }

        return $"\"{field.Replace("\"", "\"\"")}\"";
    }
}