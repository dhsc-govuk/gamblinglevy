using Application.Common.Interfaces;
using Application.Common.Text;
using Microsoft.AspNetCore.Http;

namespace Application.Common.Csv;

public class CsvParser : IBulkUploadFileParser
{
    private static readonly string[] SupportedExtensions = [".csv"];

    public bool CanParse(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return false;
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        return SupportedExtensions.Contains(extension);
    }

    public async Task<BulkUploadParseResult> ParseAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        try
        {
            var records = await ParseCsvAsync(file);
            return new BulkUploadParseResult(true, records);
        }
        catch (ArgumentException ex)
        {
            // These messages are author-written above, so they are safe to show a user verbatim.
            return new BulkUploadParseResult(false, [], ex.Message);
        }
        catch
        {
            // Anything else is an unexpected failure whose text is developer-facing and must never reach the
            // UI. Returning no message lets the caller substitute its own user-facing copy.
            return new BulkUploadParseResult(false, [], null);
        }
    }

    public static async Task<List<Dictionary<string, string>>> ParseCsvAsync(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            throw new ArgumentException("File is empty or null");
        }

        if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("File must be a CSV file (.csv)");
        }

        var records = new List<Dictionary<string, string>>();

        using var reader = new StreamReader(file.OpenReadStream());
        var headerLine = await reader.ReadLineAsync();

        if (string.IsNullOrWhiteSpace(headerLine))
        {
            throw new ArgumentException("CSV file must contain a header row");
        }

        // Lowercased so mixed-case headers (Name, ONS_Code) match the lowercase keys callers look up.
        // Done here rather than via an OrdinalIgnoreCase dictionary so a user column named "_RowNumber"
        // lowercases out of the way of the exact-case key injected below instead of clobbering it.
        var headers = ParseCsvLine(headerLine)
            .Select(h => h.Trim().Trim('"').ToLowerInvariant())
            .ToArray();

        // Normalising case can map two distinct columns (region and Region) onto one key. Rejecting here
        // matters because the row loop assigns by indexer: silently, the last column would win and could
        // overwrite a populated value with a blank one.
        var duplicates = headers
            .Where(h => !string.IsNullOrEmpty(h))
            .GroupBy(h => h)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            throw new ArgumentException($"CSV file has duplicate columns: {string.Join(", ", duplicates)}. Remove the duplicates and upload the file again.");
        }

        string? line;
        var rowNumber = 1;

        while ((line = await reader.ReadLineAsync()) != null)
        {
            rowNumber++;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var values = ParseCsvLine(line);
            var record = new Dictionary<string, string>();

            // Every header gets a key even when a short row supplies no value, so which columns a record
            // "has" is a property of the header row, not of one row's trailing commas. Callers test column
            // presence with ContainsKey, and that must not vary row to row.
            for (int i = 0; i < headers.Length; i++)
            {
                var raw = i < values.Length ? values[i].Trim().Trim('"') : string.Empty;
                record[headers[i]] = Sanitise(raw, headers[i], rowNumber);
            }

            record["_RowNumber"] = rowNumber.ToString();
            records.Add(record);
        }

        return records;
    }

    /// <summary>
    /// Applies the same character policy to a CSV cell that SanitisedStringJsonConverter applies to a JSON
    /// string (BLUM-7240 / CWE-20).
    ///
    /// It has to live here rather than in the two bulk-import handlers. The JSON converter is registered on
    /// AddJsonOptions, so it only ever sees an application/json body — the bulk-import actions are
    /// multipart/form-data uploads, and their cells reached organisations.name unexamined. A name carrying a
    /// U+202E override then renders reversed in the admin list and is written verbatim into the quarterly
    /// report ZIP a DHSC analyst opens: the Trojan Source case RequestStringNormaliser exists to prevent.
    /// Putting it in the parser means any future importer is covered the day it is written, which is the
    /// whole argument for a boundary rather than a per-caller rule.
    ///
    /// A hostile character fails the upload rather than the row. A council's spreadsheet does not contain a
    /// null byte or a bidirectional override by accident, so the file is not one to import three-quarters
    /// of — and ArgumentException is already the parser's channel for author-written copy the user sees
    /// verbatim.
    /// </summary>
    private static string Sanitise(string value, string column, int rowNumber)
    {
        if (RequestStringNormaliser.TryNormalise(value, out var normalised))
        {
            return normalised ?? string.Empty;
        }

        throw new ArgumentException(
            $"Row {rowNumber}: column '{column}' contains characters that are not allowed. Remove them and upload the file again.");
    }

    public static string[] ParseCsvLine(string line)
    {
        var values = new List<string>();
        var currentValue = string.Empty;
        var inQuotes = false;
        var i = 0;

        while (i < line.Length)
        {
            var character = line[i];

            if (character == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    currentValue += '"';
                    i += 2;
                }
                else
                {
                    inQuotes = !inQuotes;
                    i++;
                }
            }
            else if (character == ',' && !inQuotes)
            {
                values.Add(currentValue);
                currentValue = string.Empty;
                i++;
            }
            else
            {
                currentValue += character;
                i++;
            }
        }

        values.Add(currentValue);
        return values.ToArray();
    }

    public static T? GetValue<T>(Dictionary<string, string> record, string columnName)
    {
        if (!record.TryGetValue(columnName, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return default;
        }

        try
        {
            if (typeof(T) == typeof(string))
            {
                return (T)(object)value;
            }

            if (typeof(T) == typeof(bool) || typeof(T) == typeof(bool?))
            {
                if (bool.TryParse(value, out var boolResult))
                {
                    return (T)(object)boolResult;
                }

                return default;
            }

            if (typeof(T) == typeof(int) || typeof(T) == typeof(int?))
            {
                if (int.TryParse(value, out var intResult))
                {
                    return (T)(object)intResult;
                }

                return default;
            }

            if (typeof(T) == typeof(decimal) || typeof(T) == typeof(decimal?))
            {
                if (decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var decResult))
                {
                    return (T)(object)decResult;
                }

                return default;
            }

            if (typeof(T) == typeof(long) || typeof(T) == typeof(long?))
            {
                if (long.TryParse(value, out var longResult))
                {
                    return (T)(object)longResult;
                }

                return default;
            }

            if (typeof(T) == typeof(DateTime) || typeof(T) == typeof(DateTime?))
            {
                if (DateTime.TryParse(value, out var dateResult))
                {
                    return (T)(object)dateResult;
                }

                return default;
            }

            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return default;
        }
    }

    public static void ValidateRequiredColumns(Dictionary<string, string> record, params string[] requiredColumns)
    {
        var missingColumns = requiredColumns.Where(col => !record.ContainsKey(col)).ToList();

        if (missingColumns.Any())
        {
            throw new ArgumentException($"Missing required columns: {string.Join(", ", missingColumns)}");
        }
    }

    public static void ValidateRequiredValues(Dictionary<string, string> record, params string[] requiredColumns)
    {
        var emptyColumns = requiredColumns
            .Where(col => !record.TryGetValue(col, out var value) || string.IsNullOrWhiteSpace(value))
            .ToList();

        if (emptyColumns.Any())
        {
            var rowNumber = record.GetValueOrDefault("_RowNumber", "unknown");
            throw new ArgumentException($"Row {rowNumber}: Missing required values for columns: {string.Join(", ", emptyColumns)}");
        }
    }
}