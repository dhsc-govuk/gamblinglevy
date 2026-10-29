using System.Reflection;

namespace Infrastructure.Persistence.Seed.MaturityBaseline;

// One row of the 2025 Microsoft Forms maturity survey export (BLUM-7857): the identity columns plus every answer
// column keyed by its survey header. RowNumber is the 1-based line in the CSV (header = 1) so the import audit can
// point back at the source row.
public sealed record MaturityBaselineRow(
    int RowNumber,
    string Id,
    string Region,
    string LocalAuthority,
    IReadOnlyDictionary<string, string> Answers);

// Reads the embedded 2025 survey export. The file is a straight transcription of "Seeded data export
// 08092026.xlsx" with the respondent name/email columns dropped; 69 of its cells hold multi-line free text, so it
// is parsed as RFC 4180 (quoted fields may contain the delimiter, doubled quotes and line breaks) rather than
// line by line — which is why the IFormFile-oriented CsvParser is not reused here.
public static class MaturityBaselineCsv
{
    public const string ResourceFileName = "maturity-baseline-2025.csv";

    public const string IdColumn = "id";

    public const string RegionColumn = "region";

    public const string LocalAuthorityColumn = "local_authority";

    public static IReadOnlyList<MaturityBaselineRow> LoadEmbedded() => Parse(LoadResource());

    public static string LoadResource()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly
            .GetManifestResourceNames()
            .Single(name => name.EndsWith(ResourceFileName, StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static IReadOnlyList<MaturityBaselineRow> Parse(string csv)
    {
        var records = ParseRecords(csv);
        if (records.Count == 0)
        {
            return [];
        }

        var header = records[0].Select(h => h.Trim()).ToList();
        foreach (var required in new[] { IdColumn, RegionColumn, LocalAuthorityColumn })
        {
            if (!header.Contains(required, StringComparer.Ordinal))
            {
                throw new InvalidDataException($"The maturity baseline CSV has no '{required}' column.");
            }
        }

        var rows = new List<MaturityBaselineRow>();
        for (var index = 1; index < records.Count; index++)
        {
            var record = records[index];
            if (record.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var id = string.Empty;
            var region = string.Empty;
            var localAuthority = string.Empty;
            var answers = new Dictionary<string, string>(StringComparer.Ordinal);

            for (var column = 0; column < header.Count; column++)
            {
                var name = header[column];
                if (name.Length == 0)
                {
                    continue;
                }

                var value = column < record.Count ? record[column] : string.Empty;
                switch (name)
                {
                    case IdColumn:
                        id = value.Trim();
                        break;
                    case RegionColumn:
                        region = value.Trim();
                        break;
                    case LocalAuthorityColumn:
                        localAuthority = value.Trim();
                        break;
                    default:
                        answers[name] = value;
                        break;
                }
            }

            rows.Add(new MaturityBaselineRow(index + 1, id, region, localAuthority, answers));
        }

        return rows;
    }

    // RFC 4180: fields separated by commas, records by CRLF or LF, a field may be wrapped in double quotes, inside
    // which a quote is doubled and delimiters and line breaks are literal.
    public static IReadOnlyList<IReadOnlyList<string>> ParseRecords(string text)
    {
        var records = new List<IReadOnlyList<string>>();
        var fields = new List<string>();
        var field = new System.Text.StringBuilder();
        var inQuotes = false;
        var recordHasContent = false;

        for (var position = 0; position < text.Length; position++)
        {
            var character = text[position];

            if (inQuotes)
            {
                if (character == '"')
                {
                    if (position + 1 < text.Length && text[position + 1] == '"')
                    {
                        field.Append('"');
                        position++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(character);
                }

                continue;
            }

            switch (character)
            {
                case '"':
                    inQuotes = true;
                    recordHasContent = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    recordHasContent = true;
                    break;
                case '\r':
                    if (position + 1 < text.Length && text[position + 1] == '\n')
                    {
                        position++;
                    }

                    EndRecord();
                    break;
                case '\n':
                    EndRecord();
                    break;
                default:
                    field.Append(character);
                    recordHasContent = true;
                    break;
            }
        }

        if (inQuotes)
        {
            throw new InvalidDataException("The maturity baseline CSV ends inside a quoted field.");
        }

        if (recordHasContent || field.Length > 0)
        {
            EndRecord();
        }

        return records;

        void EndRecord()
        {
            fields.Add(field.ToString());
            field.Clear();
            records.Add(fields.ToList());
            fields.Clear();
            recordHasContent = false;
        }
    }
}