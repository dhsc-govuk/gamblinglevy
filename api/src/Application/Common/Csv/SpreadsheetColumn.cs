namespace Application.Common.Csv;

// Spreadsheet column letters for a zero-based column index: 0 is A, 25 is Z, 26 is AA, 701 is ZZ, 702 is AAA.
public static class SpreadsheetColumn
{
    public static string Letter(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        var letters = string.Empty;
        var remaining = index + 1;
        while (remaining > 0)
        {
            var offset = (remaining - 1) % 26;
            letters = (char)('A' + offset) + letters;
            remaining = (remaining - 1) / 26;
        }

        return letters;
    }
}