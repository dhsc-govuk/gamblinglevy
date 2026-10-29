using Application.Common.Csv;

namespace Application.Submissions.Offline;

// An uploaded offline file, parsed and located: every record with its spreadsheet row, the key row (row 3 of the
// template, wherever it now is) and the column the identity cell sits in.
public sealed record UploadedOfflineFile(
    IReadOnlyList<CsvRecord> Records,
    char Delimiter,
    CsvRecord KeyRow,
    int ReferenceColumnIndex,
    OfflineIdentity Identity)
{
    // Column key → the column it is in this file, from the key row. Duplicated keys are left out and reported by
    // DuplicateKeys, so a person who copied a column is told rather than having one copy silently win.
    public IReadOnlyDictionary<string, int> ColumnIndexByKey { get; } = KeyRow.Cells
        .Select((cell, index) => (Key: cell.Trim(), Index: index))
        .Where(cell => cell.Index != ReferenceColumnIndex && cell.Key.Length > 0)
        .GroupBy(cell => cell.Key, StringComparer.Ordinal)
        .Where(group => group.Count() == 1)
        .ToDictionary(group => group.Key, group => group.Single().Index, StringComparer.Ordinal);

    public IReadOnlyList<string> DuplicateKeys { get; } = KeyRow.Cells
        .Select((cell, index) => (Key: cell.Trim(), Index: index))
        .Where(cell => cell.Index != ReferenceColumnIndex && cell.Key.Length > 0)
        .GroupBy(cell => cell.Key, StringComparer.Ordinal)
        .Where(group => group.Count() > 1)
        .Select(group => group.Key)
        .ToList();
}

public enum OfflineFileProblem
{
    Unreadable,
    NotATemplate,
}

// Reads an uploaded file far enough to know what it claims to be. Excel saves "CSV" with a semicolon or a tab in some
// locales, so each delimiter is tried until one gives a record carrying the identity cell. The identity cell is
// found by its content rather than its position: sorting the sheet in Excel ("my data has headers") moves rows 2 and 3
// down among the answers, and inserting a column moves column A.
public static class OfflineFileReader
{
    private static readonly char[] Delimiters = [',', ';', '\t'];

    public static (UploadedOfflineFile? File, OfflineFileProblem? Problem) Read(byte[] content)
    {
        string text;
        try
        {
            text = CsvTextDecoder.Decode(content);
        }
        catch (Exception)
        {
            return (null, OfflineFileProblem.Unreadable);
        }

        var parsedAny = false;
        var sawMalformedIdentity = false;
        foreach (var delimiter in Delimiters)
        {
            IReadOnlyList<CsvRecord> records;
            try
            {
                records = CsvRecordReader.Read(text, delimiter);
            }
            catch (FormatException)
            {
                continue;
            }

            parsedAny = true;
            foreach (var record in records)
            {
                for (var index = 0; index < record.Cells.Count; index++)
                {
                    var cell = OfflineCellValues.StripFormulaGuard(record.Cells[index].Trim());
                    if (!OfflineIdentity.LooksLikeIdentity(cell))
                    {
                        continue;
                    }

                    // Read with the wrong delimiter, the identity cell runs on into its neighbours and does not
                    // parse; the right delimiter is still to come.
                    var identity = OfflineIdentity.TryParse(cell);
                    if (identity is null)
                    {
                        sawMalformedIdentity = true;
                        continue;
                    }

                    return (new UploadedOfflineFile(records, delimiter, record, index, identity), null);
                }
            }
        }

        return (null, parsedAny || sawMalformedIdentity ? OfflineFileProblem.NotATemplate : OfflineFileProblem.Unreadable);
    }
}