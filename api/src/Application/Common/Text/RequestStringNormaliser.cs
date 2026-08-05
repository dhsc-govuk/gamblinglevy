using System.Text;

namespace Application.Common.Text;

/// <summary>
/// Decides what a string arriving in a request body is allowed to contain.
///
/// Azure Front Door blocks the well-known injection strings, but injection characters passed straight
/// through to the application layer (BLUM-7240 / CWE-20). Nothing in the API stripped control characters,
/// filtered null bytes or handled bidirectional overrides, so a NUL truncates a value in anything
/// downstream that speaks C, a bare CR forges a line in a log or a CSV cell, and the Trojan Source
/// overrides (CVE-2021-42574) make a stored answer render as something other than what was submitted — in
/// an export a DHSC analyst reads.
///
/// The split between rejecting and normalising is about whether a real person could have produced the
/// character by accident. Nobody types a NUL or a right-to-left override, so those are a 400. Word leaves
/// a vertical tab where somebody pressed Shift+Enter and web pages leave zero-width spaces and BOMs in a
/// paste, so those are quietly repaired rather than turned into an inexplicable error on a 10,000-character
/// answer somebody just spent an hour writing.
///
/// Code points are written numerically rather than as escape sequences on purpose: this file is the
/// authoritative statement of the policy, and a reader should not have to decode a literal to check it.
/// </summary>
public static class RequestStringNormaliser
{
    private const char VerticalTab = (char)0x000B;
    private const char FormFeed = (char)0x000C;
    private const char Delete = (char)0x007F;

    // C1 controls. Legal in a .NET string, meaningless in a form field, and historically a way to smuggle
    // an escape sequence past a filter that only looked at C0.
    private const char C1RangeStart = (char)0x0080;
    private const char C1RangeEnd = (char)0x009F;

    // Trojan Source (CVE-2021-42574, Unicode UTR #36): characters that change how text renders without
    // changing what it says.
    private const char BidiEmbeddingStart = (char)0x202A;   // LRE, RLE, PDF, LRO, RLO
    private const char BidiEmbeddingEnd = (char)0x202E;
    private const char BidiIsolateStart = (char)0x2066;     // LRI, RLI, FSI, PDI
    private const char BidiIsolateEnd = (char)0x2069;

    private const char ZeroWidthSpace = (char)0x200B;
    private const char LeftToRightMark = (char)0x200E;
    private const char RightToLeftMark = (char)0x200F;
    private const char WordJoinerStart = (char)0x2060;      // word joiner and the invisible operators
    private const char WordJoinerEnd = (char)0x2064;
    private const char ByteOrderMark = (char)0xFEFF;

    /// <summary>
    /// Returns false when the value contains a character no legitimate caller sends; the caller then answers
    /// 400 with fixed copy. No reason is returned on purpose — anything specific enough to be useful would
    /// have to be built out of the caller's own input.
    /// </summary>
    public static bool TryNormalise(string? candidate, out string? normalised)
    {
        if (candidate is null)
        {
            normalised = null;
            return true;
        }

        if (ContainsRejectedCharacter(candidate))
        {
            normalised = null;
            return false;
        }

        var builder = new StringBuilder(candidate.Length);

        for (var i = 0; i < candidate.Length; i++)
        {
            var current = candidate[i];

            // CRLF and a lone CR both become LF: one line ending in storage, in exports and in logs, and so
            // no way to forge a record boundary in any of the three.
            if (current == '\r')
            {
                builder.Append('\n');
                if (i + 1 < candidate.Length && candidate[i + 1] == '\n')
                {
                    i++;
                }

                continue;
            }

            // Vertical tab and form feed are what Word and PDF put where a person pressed Shift+Enter, so
            // they mean "new line" rather than "attack".
            if (current is VerticalTab or FormFeed)
            {
                builder.Append('\n');
                continue;
            }

            if (IsInvisibleFormatting(current))
            {
                continue;
            }

            builder.Append(current);
        }

        // Trimmed last, so "abc\r\n   " and "abc" are the same answer. Validators that already measure
        // .Trim().Length keep working unchanged, and a whitespace-only value still arrives as "" for
        // NotEmpty() to reject rather than slipping through as a non-empty string.
        normalised = builder.ToString().Trim();
        return true;
    }

    public static bool ContainsRejectedCharacter(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];

            if (IsRejected(current))
            {
                return true;
            }

            if (!char.IsSurrogate(current))
            {
                continue;
            }

            // A non-BMP character — an emoji, most obviously — is a high surrogate followed by a low one,
            // and is perfectly legitimate. Only an unpaired half is rejected: it cannot be encoded as UTF-8,
            // so it would either fail or be silently replaced somewhere downstream.
            if (!char.IsHighSurrogate(current) || i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
            {
                return true;
            }

            i++;
        }

        return false;
    }

    // Tab, line feed and carriage return survive: the free-text answer fields hold 10,000 characters of
    // prose and tabs and newlines are part of it. CR is folded to LF above rather than kept as-is.
    private static bool IsRejected(char value)
    {
        return (value < ' ' && value is not ('\t' or '\n' or '\r' or VerticalTab or FormFeed))
            || value == Delete
            || value is >= C1RangeStart and <= C1RangeEnd
            || value is >= BidiEmbeddingStart and <= BidiEmbeddingEnd
            || value is >= BidiIsolateStart and <= BidiIsolateEnd;
    }

    // U+200C ZWNJ and U+200D ZWJ are deliberately absent. They join and separate letters — Persian and
    // Indic orthography need them, and so do emoji sequences — so stripping them would corrupt a legitimate
    // name. The attack is the direction controls, which are rejected above and have no such use.
    private static bool IsInvisibleFormatting(char value)
    {
        return value is ZeroWidthSpace or LeftToRightMark or RightToLeftMark or ByteOrderMark
            || value is >= WordJoinerStart and <= WordJoinerEnd;
    }
}