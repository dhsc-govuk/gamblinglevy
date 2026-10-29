using System.Text;

namespace Application.Common.Csv;

// One record of a CSV file and the spreadsheet row it came from.
public sealed record CsvRecord(int RowNumber, IReadOnlyList<string> Cells)
{
    public string CellAt(int index) => index >= 0 && index < Cells.Count ? Cells[index] : string.Empty;

    public bool IsBlank => Cells.All(string.IsNullOrWhiteSpace);
}

// An RFC-4180 reader for files people edit in a spreadsheet: quoted delimiters, doubled quotes and line breaks
// inside a quoted cell. CsvParser (the bulk imports) reads line by line and so splits a multi-line answer into two
// records; this one keeps it whole.
//
// RowNumber is the row the spreadsheet shows, which is what an error message has to name: records are numbered
// from 1, an empty line still takes a row, and a cell that spans several lines is still one row.
public static class CsvRecordReader
{
    public static IReadOnlyList<CsvRecord> Read(string text, char delimiter = ',')
    {
        ArgumentNullException.ThrowIfNull(text);

        var records = new List<CsvRecord>();
        var cells = new List<string>();
        var cell = new StringBuilder();
        var inQuotes = false;
        var recordStarted = false;
        var index = 0;

        void EndCell()
        {
            cells.Add(cell.ToString());
            cell.Clear();
        }

        void EndRecord()
        {
            EndCell();
            records.Add(new CsvRecord(records.Count + 1, cells.ToList()));
            cells.Clear();
            recordStarted = false;
        }

        while (index < text.Length)
        {
            var character = text[index];

            if (character is not ('\r' or '\n') || inQuotes)
            {
                recordStarted = true;
            }

            if (inQuotes)
            {
                if (character == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        cell.Append('"');
                        index += 2;
                        continue;
                    }

                    inQuotes = false;
                    index++;
                    continue;
                }

                // A line break inside a quoted cell is part of the answer. Stored answers use LF, so CRLF and a
                // lone CR fold to LF here rather than later.
                if (character == '\r')
                {
                    cell.Append('\n');
                    index += index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1;
                    continue;
                }

                cell.Append(character);
                index++;
                continue;
            }

            if (character == '"' && cell.Length == 0)
            {
                inQuotes = true;
                index++;
                continue;
            }

            if (character == delimiter)
            {
                EndCell();
                index++;
                continue;
            }

            if (character is '\r' or '\n')
            {
                EndRecord();
                index += character == '\r' && index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1;
                continue;
            }

            cell.Append(character);
            index++;
        }

        if (inQuotes)
        {
            throw new FormatException("The file ends inside a quoted cell.");
        }

        // A final line break ends the last record; it does not start another one.
        if (recordStarted)
        {
            EndRecord();
        }

        return records;
    }
}