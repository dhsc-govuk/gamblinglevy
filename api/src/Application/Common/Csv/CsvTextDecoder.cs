using System.Text;

namespace Application.Common.Csv;

// Turns the bytes of a CSV a person saved from a spreadsheet back into text. Excel offers two "CSV" formats and
// people pick either: "CSV UTF-8" writes UTF-8 with a BOM, while plain "CSV (Comma delimited)" writes the machine's
// ANSI code page — Windows-1252 on a UK machine — with no marker at all. So: trust a BOM, then try strict UTF-8
// (which rejects 1252's lone high bytes such as 0xA3 "£" and 0x92 "’"), and only then fall back to 1252.
//
// The fallback has to be 1252 and not Latin-1: Latin-1 maps 0x80-0x9F onto the C1 control characters, so a curly
// apostrophe would arrive as U+0092 and RequestStringNormaliser would reject the whole cell.
public static class CsvTextDecoder
{
    private const int Windows1252CodePage = 1252;

    public static string Decode(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (StartsWith(content, 0xEF, 0xBB, 0xBF))
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false).GetString(content, 3, content.Length - 3);
        }

        if (StartsWith(content, 0xFF, 0xFE))
        {
            return Encoding.Unicode.GetString(content, 2, content.Length - 2);
        }

        if (StartsWith(content, 0xFE, 0xFF))
        {
            return Encoding.BigEndianUnicode.GetString(content, 2, content.Length - 2);
        }

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(content);
        }
        catch (DecoderFallbackException)
        {
            return CodePagesEncodingProvider.Instance.GetEncoding(Windows1252CodePage)!.GetString(content);
        }
    }

    private static bool StartsWith(byte[] content, params byte[] prefix) =>
        content.Length >= prefix.Length && content.AsSpan(0, prefix.Length).SequenceEqual(prefix);
}