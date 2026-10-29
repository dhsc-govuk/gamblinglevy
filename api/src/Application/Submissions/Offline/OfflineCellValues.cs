using System.Globalization;
using System.Text;
using Application.Common.Text;
using Application.Common.Validation;
using Application.Submissions.Protocol;
using Domain.Forms;

namespace Application.Submissions.Offline;

// Converts between a stored answer and what a spreadsheet cell shows. The template writes choices as their labels
// (a person reads "Local charities", not "local_charities") and money as plain whole pounds; the upload accepts what
// a person plausibly types back: a label or a value in any case, curly or straight quotes, several choices
// separated by semicolons, and "£1,500" or "1500.00" for money. Anything it cannot turn into a stored value is an
// error on that cell rather than a guess.
public static class OfflineCellValues
{
    public const string NotApplicable = "N/A";
    public const string ChoiceSeparator = "; ";

    private const int MaxQuotedLength = 60;

    public static bool IsBlankOrNotApplicable(string? cell)
    {
        var trimmed = cell?.Trim() ?? string.Empty;
        return trimmed.Length == 0
            || trimmed.Equals("N/A", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("NA", StringComparison.OrdinalIgnoreCase);
    }

    // CsvWriter.Escape puts a ' in front of a cell starting with = + - @ tab or CR so Excel reads it as text; the
    // spreadsheet keeps that ' as part of the value, so it comes back here and is taken off again. Only in front of
    // one of those characters — anywhere else a leading ' is the person's own.
    public static string StripFormulaGuard(string cell) =>
        cell.Length > 1 && cell[0] == '\'' && cell[1] is '=' or '+' or '-' or '@' or '\t' or '\r' ? cell[1..] : cell;

    // A stored answer as the template shows it. A withdrawn option still shows its label (BLUM-9104), and the upload
    // then refuses it, because Parse matches only the options an LA may pick — just as the web form does.
    public static string Display(ResolvedQuestion question, string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return string.Empty;
        }

        switch (question.QuestionType)
        {
            case QuestionTypes.Radio:
            case QuestionTypes.Select:
                return question.Options.Concat(question.WithdrawnOptions).FirstOrDefault(option => option.Value == stored.Trim())?.Label ?? stored;

            case QuestionTypes.Checkbox:
                var labelled = question.Options.Concat(question.WithdrawnOptions).ToList();
                var selected = stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
                var labels = labelled.Where(option => selected.Contains(option.Value)).Select(option => option.Label).ToList();
                labels.AddRange(selected.Where(value => labelled.All(option => option.Value != value)));
                return string.Join(ChoiceSeparator, labels);

            // An amount stored with a misplaced separator ("5,10", saved before BLUM-8955) is shown as stored, for
            // the upload to refuse, rather than as 510.
            case QuestionTypes.Currency:
                return MoneyAnswer.TryRead(stored, out var amount) ? FormatMoney(amount) : stored;

            default:
                return stored;
        }
    }

    // Whole pounds without a decimal point; anything else exactly as typed, never rounded — "0.001" must reach the
    // validators as pence and be refused, not become £0.
    public static string FormatMoney(decimal amount) =>
        amount == decimal.Truncate(amount)
            ? amount.ToString("0", CultureInfo.InvariantCulture)
            : amount.ToString(CultureInfo.InvariantCulture);

    // A cell as the value to store. The caller has already dealt with blank, N/A and the formula guard.
    public static OfflineCellParse Parse(ResolvedQuestion question, string cell)
    {
        if (!RequestStringNormaliser.TryNormalise(cell, out var normalised))
        {
            return OfflineCellParse.Failed("Retype this answer. It contains characters we cannot accept");
        }

        var value = normalised ?? string.Empty;
        if (value.Length == 0)
        {
            return OfflineCellParse.Blank;
        }

        return question.QuestionType switch
        {
            QuestionTypes.Radio or QuestionTypes.Select => ParseChoice(question, value),
            QuestionTypes.Checkbox => ParseChoices(question, value),
            QuestionTypes.Number => ParseNumber(question, value),
            QuestionTypes.Currency => ParseMoney(value),
            _ => OfflineCellParse.Stored(value),
        };
    }

    private static OfflineCellParse ParseChoice(ResolvedQuestion question, string value)
    {
        var option = Match(question, value);
        if (option is not null)
        {
            return OfflineCellParse.Stored(option.Value);
        }

        return OfflineCellParse.Failed(question.Options.Count == 2
            ? $"Enter ‘{question.Options[0].Label}’ or ‘{question.Options[1].Label}’"
            : "Enter one of the answers listed in row 2");
    }

    // Choices are separated by semicolons or line breaks, never commas: option labels contain commas ("Voluntary,
    // community and social enterprise organisations"). A part that matches nothing may still be a comma-separated
    // list, so it is tried split on commas as a fallback — accepted only when every piece is an option.
    private static OfflineCellParse ParseChoices(ResolvedQuestion question, string value)
    {
        var selected = new HashSet<string>(StringComparer.Ordinal);
        var parts = value.Split([';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in parts)
        {
            var option = Match(question, part);
            if (option is not null)
            {
                selected.Add(option.Value);
                continue;
            }

            var pieces = part.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(piece => Match(question, piece))
                .ToList();
            if (pieces.Count > 1 && pieces.All(piece => piece is not null))
            {
                foreach (var piece in pieces)
                {
                    selected.Add(piece!.Value);
                }

                continue;
            }

            return OfflineCellParse.Failed($"‘{Quote(part)}’ is not one of the answers listed in row 2. Separate answers with a semicolon (;)");
        }

        if (selected.Count == 0)
        {
            return OfflineCellParse.Blank;
        }

        var ordered = question.Options.Where(option => selected.Contains(option.Value)).Select(option => option.Value);
        return OfflineCellParse.Stored(string.Join(',', ordered));
    }

    // Thousands separators and a percentage sign are dropped; whether what is left is a whole number in range is
    // SubmissionAnswerValidator's call, so the message is the same as on the web. A comma or a space anywhere else
    // stays where it is for that validator to refuse (BLUM-8955): "5,10" is not 510.
    private static OfflineCellParse ParseNumber(ResolvedQuestion question, string value)
    {
        var typed = value.Trim();
        if (question.IsPercentage && typed.EndsWith('%'))
        {
            typed = typed[..^1];
        }

        var cleaned = MoneyAnswer.Normalise(typed);
        return cleaned.Length == 0 ? OfflineCellParse.Blank : OfflineCellParse.Stored(cleaned);
    }

    // "£1,500", "1 500", "1500.00" and "-5" all parse; the amount rules (not negative, no pence, the cap) are the
    // validators', so a spreadsheet gets the same messages as the web. What is stored is the plain amount. A comma
    // or a space that is not a thousands separator is refused here (BLUM-8955): the validators are handed a number,
    // so they could not tell that "5,10" had been read as 510.
    private static OfflineCellParse ParseMoney(string value) =>
        MoneyAnswer.TryRead(value.Replace("£", string.Empty), out var amount)
            ? OfflineCellParse.Stored(FormatMoney(amount))
            : OfflineCellParse.Failed("Enter a whole number of pounds, like 1500");

    private static ResolvedQuestionOption? Match(ResolvedQuestion question, string candidate)
    {
        var folded = Fold(candidate);
        if (folded.Length == 0)
        {
            return null;
        }

        return question.Options.FirstOrDefault(option => Fold(option.Label) == folded)
            ?? question.Options.FirstOrDefault(option => Fold(option.Value) == folded);
    }

    // Case, curly quotes, runs of whitespace and a pair of surrounding quotes (people copy ‘Yes’ out of row 2 with
    // its quotes) do not change which option a cell means.
    private static string Fold(string value)
    {
        var text = value.Trim();
        if (text.Length >= 2 && IsQuote(text[0]) && IsQuote(text[^1]))
        {
            text = text[1..^1].Trim();
        }

        var builder = new StringBuilder(text.Length);
        var previousWasSpace = false;
        foreach (var character in text)
        {
            var mapped = character switch
            {
                '‘' or '’' or '`' => '\'',
                '“' or '”' => '"',
                _ => char.ToLowerInvariant(character),
            };

            if (char.IsWhiteSpace(mapped))
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                }

                previousWasSpace = true;
                continue;
            }

            builder.Append(mapped);
            previousWasSpace = false;
        }

        return builder.ToString().Trim();
    }

    private static bool IsQuote(char character) => character is '\'' or '"' or '‘' or '’' or '“' or '”';

    private static string Quote(string value) =>
        value.Length <= MaxQuotedLength ? value : value[..MaxQuotedLength] + "…";
}

// What a cell turned into: a stored value, nothing (blank), or a message for the person.
public sealed record OfflineCellParse(string? Value, string? Error)
{
    public static readonly OfflineCellParse Blank = new(null, null);

    public bool IsError => Error is not null;

    public static OfflineCellParse Stored(string value) => new(value, null);

    public static OfflineCellParse Failed(string message) => new(null, message);
}