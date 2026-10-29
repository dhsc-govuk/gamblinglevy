using System.Globalization;
using Api.Dtos;
using Application.Common.Validation;

namespace Api.Services.ReportSnapshot;

// Single C# source of truth for how a quarterly-report answer is shown, ported from the frontend so the downloaded
// "copy for your records" matches what the LA reviewed on screen. Pinned by ReportAnswerPresenterTests; the matching
// frontend rules live in display.tsx (renderReportAnswer) and views/Activities/answers.ts (resolveAnswerDisplay) —
// keep the two in step. Which questions are shown is decided by Application.Forms.Answers.AnswerVisibility, as for
// every validator.
//
// The core overloads take the question's parts rather than a DTO because two question shapes render through them:
// the quarterly report's ReportQuestionDto and the activity record's FormQuestionDto (the activity-record export,
// BLUM-7648). One port, not two.
public static class ReportAnswerPresenter
{
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");

    // Withdrawn options label too: an answer given before DHSC withdrew its option still shows what it meant (BLUM-9104).
    public static string RenderAnswer(ReportQuestionDto question, string? value) =>
        RenderAnswer(
            question.QuestionType,
            question.Options.Concat(question.WithdrawnOptions).Select(option => (option.Value, option.Label)).ToList(),
            question.Suffix,
            value);

    // Port of the frontend resolveAnswerDisplay: choice values are mapped to their option labels (checkbox values are
    // comma-split and each part mapped, then joined), falling back to the raw value when no option matches, and a
    // Currency answer is shown as pounds the way the activity view shows it. The unit suffix (e.g. "%") is appended
    // when present — the export deliberately keeps units the on-screen summary omits.
    public static string RenderAnswer(
        string questionType,
        IReadOnlyList<(string Value, string Label)> options,
        string? suffix,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var rendered = questionType switch
        {
            "Checkbox" => string.Join(
                ", ",
                value.Split(',').Select(part => part.Trim()).Where(part => part.Length > 0).Select(LabelFor)),
            "Radio" or "Select" => LabelFor(value),
            "Currency" => FormatCurrency(value),
            _ => value,
        };

        return string.IsNullOrEmpty(suffix) ? rendered : $"{rendered}{suffix}";

        string LabelFor(string optionValue)
        {
            foreach (var option in options)
            {
                if (option.Value == optionValue)
                {
                    return option.Label;
                }
            }

            return optionValue;
        }
    }

    // Port of formatGBP(readAmount(value)) in client/src/helpers/currency.ts: "£" is dropped and thousands
    // separators are read through, then a number renders with en-GB grouping and at most two decimals ("25000" →
    // "£25,000"). Anything that is not a number is shown as typed. That is a row saved before its question became a
    // Currency one, or one saved while a misplaced separator was still stripped (BLUM-8955): "5,10" was stored as
    // typed and shown as £510, and what the LA meant by it is not ours to guess.
    private static string FormatCurrency(string value)
    {
        if (!MoneyAnswer.TryRead(value.Replace("£", string.Empty), out var amount))
        {
            return value;
        }

        var magnitude = Math.Abs(amount).ToString("#,##0.##", EnGb);
        return amount < 0 ? $"-£{magnitude}" : $"£{magnitude}";
    }
}