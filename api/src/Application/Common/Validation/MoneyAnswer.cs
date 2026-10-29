using System.Globalization;

namespace Application.Common.Validation;

// How a money question is named in its own error messages. The protocol words them around two forms of one subject,
// the pair FundingSpendValidator passes to its ValidateAmount: Sentence starts a message ("Amount agreed must not
// include £"), Inline reads mid-sentence ("Enter an amount of 1 or more for the amount you have agreed").
public readonly record struct MoneySubject(string Sentence, string Inline)
{
    // A question the protocol gives no subject is named by its own label, quoted, in both positions.
    public static MoneySubject Quoting(string label) => new($"“{label}”", $"“{label}”");
}

// The rules a "Currency" answer is held to, in one place because two validators enforce them — the activity
// record's ActivityAnswerValidator and the quarterly report's SubmissionAnswerValidator, since Currency is a
// Domain.Forms.QuestionType and DcFormQuestion reuses that type. Mirrored client-side by validateCurrencyAnswer
// (client/src/views/Activities/answers.ts) on top of the shared rules in client/src/helpers/currency.ts.
//
// Distinct from FundingSpendValidator, which enforces the same rules on an already-parsed decimal: "Record funding
// spent" posts numbers, so it never sees a typed "£" or a grouping separator. An answer arrives as the raw string
// the user typed, so those two rules are enforceable here and are.
public static class MoneyAnswer
{
    // The cap FundingSpendValidator already applies to every money amount: comfortably inside the numeric(18,2)
    // column and far above any realistic levy commitment, so an over-large amount is a clean 422 rather than a
    // DbUpdateException on save.
    public const decimal MaxAmount = 1_000_000_000m;

    // Null when the value is a whole number of pounds between £0 and MaxAmount; otherwise the message to show,
    // worded around the question's subject. A question that has a minimum is held to it (BLUM-8955) — by both
    // validators, because the client's validateCurrencyAnswer applies whatever minValue a question arrives with.
    public static string? Validate(string value, MoneySubject subject, decimal? minValue)
    {
        if (value.Contains('£'))
        {
            return $"{subject.Sentence} must not include £";
        }

        var normalised = Normalise(value);

        if (!IsWholePounds(normalised))
        {
            // Sign before shape, where there is a minimum to hold it to: "-5" is an amount below it, and "that is
            // not a number" would send the user looking for a typo.
            if (minValue is { } floor && IsNegativeWholePounds(normalised))
            {
                return BelowMinimum(floor, subject);
            }

            // The example must be an amount the question would accept, so "0" goes when there is a minimum.
            return LooksLikePence(normalised)
                ? $"{subject.Sentence} must be a whole number and not include pence"
                : $"Enter a number for {subject.Inline}, for example {(minValue >= 1 ? "1500" : "0 or 1500")}";
        }

        // A digit string long enough to overflow decimal is over the cap by definition, so a failed parse takes the
        // same arm rather than throwing.
        if (!decimal.TryParse(normalised, out var amount) || amount > MaxAmount)
        {
            return $"{subject.Sentence} must be £1,000,000,000 or less";
        }

        return minValue is { } minimum && amount < minimum ? BelowMinimum(minimum, subject) : null;
    }

    // A comma or a space is a thousands separator or it is refused (BLUM-8955). Every separator used to be stripped
    // wherever it sat, so "5,10" — a decimal comma — validated, and was later displayed, as 510. A grouped amount
    // comes back as its digits; anything else comes back as typed, for the shape checks to name.
    //
    // ANY whitespace counts as a separator, not just the ASCII space: the client matches with JavaScript's \s,
    // which covers the non-breaking space that Word and Excel use as a thousands separator. Accepting less here
    // means "25 000" pasted from a spreadsheet passes the client and comes back a 422 the user cannot see on the
    // field. \s and char.IsWhiteSpace differ only in characters that never get this far: RequestStringNormaliser
    // takes out U+FEFF, which only \s matches, and refuses U+0085 and U+001C–U+001F, which only this does.
    public static string Normalise(string value)
    {
        var typed = value.Trim();
        return IsGrouped(typed) ? new(typed.Where(char.IsAsciiDigit).ToArray()) : typed;
    }

    // The number a typed amount stands for, for a reader that is not validating it: the display ports, and a
    // spreadsheet cell whose amount rules belong to the validators. Those meet what Validate refuses by name — a
    // sign, pence — and "-1,500" and "1,500.50" are still the numbers they say, so the pounds are read through
    // Normalise and the sign and the pence are kept. A misplaced separator is not read at all: "5,10" is false,
    // not 510. Mirrored client-side by readAmount (client/src/helpers/currency.ts).
    public static bool TryRead(string value, out decimal amount)
    {
        var typed = value.Trim();
        var sign = typed.Length > 0 && typed[0] is '-' or '+' ? typed[..1] : string.Empty;
        var unsigned = typed[sign.Length..].TrimStart();
        var point = unsigned.IndexOf('.');
        var pounds = point < 0 ? unsigned : unsigned[..point];
        var pence = point < 0 ? string.Empty : unsigned[point..];

        return decimal.TryParse(
            sign + Normalise(pounds) + pence,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out amount);
    }

    // Written without the scale a numeric column gives it: 1.00 reads "1", as the client's number does.
    private static string BelowMinimum(decimal minimum, MoneySubject subject) =>
        $"Enter an amount of {minimum.ToString("0.##", CultureInfo.InvariantCulture)} or more for {subject.Inline}";

    private static bool IsWholePounds(string value) =>
        value.Length > 0 && value.All(char.IsAsciiDigit);

    private static bool IsNegativeWholePounds(string value) =>
        value.StartsWith('-') && IsWholePounds(Normalise(value[1..]));

    // "1,500", "25 000", "1,000,000": one to three digits that do not start with a zero, then groups of exactly
    // three. The client's GROUPED_AMOUNT pattern is the same rule.
    private static bool IsGrouped(string value)
    {
        var first = value.TakeWhile(char.IsAsciiDigit).Count();
        if (first is < 1 or > 3 || first == value.Length || value[0] == '0')
        {
            return false;
        }

        for (var separator = first; separator < value.Length; separator += 4)
        {
            if (!IsSeparator(value[separator])
                || separator + 4 > value.Length
                || !IsWholePounds(value.Substring(separator + 1, 3)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSeparator(char character) => character == ',' || char.IsWhiteSpace(character);

    // A well-formed amount followed by pence. The decimal mark may be a comma as well as a full stop: "5,10" is
    // pence to the person who typed it, and telling them it is not a number would be wrong.
    private static bool LooksLikePence(string value)
    {
        var separator = value.LastIndexOfAny(['.', ',']);
        return separator > 0
            && separator < value.Length - 1
            && (IsWholePounds(value[..separator]) || IsGrouped(value[..separator]))
            && IsWholePounds(value[(separator + 1)..]);
    }
}