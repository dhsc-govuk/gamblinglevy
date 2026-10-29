using System.Globalization;
using System.Text;

namespace Api.Services.ReportSnapshot;

// The one slug rule every export file name goes through (hoisted from QuarterlyReportExportService when the
// maturity assessment needed the same behaviour).
//
// Letters and digits survive as typed — the name stays readable and title-cased — and every run of anything else
// (spaces, commas, "&", "/") collapses to one hyphen. Accented letters are FOLDED to their base letter first, not
// treated as punctuation ("Ynys Môn" stays "Ynys-Mon"). Apostrophes are dropped rather than collapsed, because
// they sit inside a word ("King's Lynn" → "Kings-Lynn").
public static class ExportFileName
{
    // Long enough for the longest real organisation name, short enough that a pathological one cannot produce an
    // unwieldy Content-Disposition value or a filename the user's filesystem refuses.
    public const int MaxNameSegmentLength = 80;

    public static string Slug(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
            {
                builder.Append(character);
            }
            else if (character is '\'' or '’')
            {
                continue;
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().TrimEnd('-');
    }

    public static string NameSegment(string value)
    {
        var name = Slug(value);
        return name.Length > MaxNameSegmentLength ? name[..MaxNameSegmentLength].TrimEnd('-') : name;
    }
}