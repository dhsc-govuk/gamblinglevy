using Api.Dtos;

namespace Api.Services.ReportSnapshot;

// Single C# source of truth for how a quarterly-report answer is shown and which questions are visible, ported from
// the frontend so the downloaded "copy for your records" matches what the LA reviewed on screen. Pinned by
// ReportAnswerPresenterTests; the matching frontend rules live in display.tsx (renderReportAnswer / visibleQuestionRows)
// and GovUKDynamicQuestionRenderer (isQuestionVisible) — keep the two in step.
public static class ReportAnswerPresenter
{
    // Port of the frontend isQuestionVisible: a question with no conditional rule is always shown; otherwise it is
    // hidden until its parent is answered, and visible only when any of the (comma-separated) parent values is in the
    // (comma-separated) allow-list. Both sides are trimmed; a checkbox parent may hold several comma-joined values.
    public static bool IsVisible(ReportQuestionDto question, IReadOnlyDictionary<string, string?> answers)
    {
        if (string.IsNullOrEmpty(question.ConditionalQuestionCode) || string.IsNullOrEmpty(question.ConditionalValue))
        {
            return true;
        }

        if (!answers.TryGetValue(question.ConditionalQuestionCode, out var parentValue) || string.IsNullOrEmpty(parentValue))
        {
            return false;
        }

        var allowed = question.ConditionalValue.Split(',').Select(value => value.Trim());
        var parentValues = parentValue.Split(',').Select(value => value.Trim()).ToHashSet(StringComparer.Ordinal);

        return allowed.Any(parentValues.Contains);
    }

    // Port of the frontend resolveAnswerDisplay: choice values are mapped to their option labels (checkbox values are
    // comma-split and each part mapped, then joined), falling back to the raw value when no option matches. The unit
    // suffix (e.g. "%") is appended when present — the export deliberately keeps units the on-screen summary omits.
    public static string RenderAnswer(ReportQuestionDto question, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var rendered = question.QuestionType switch
        {
            "Checkbox" => string.Join(
                ", ",
                value.Split(',').Select(part => part.Trim()).Where(part => part.Length > 0).Select(LabelFor)),
            "Radio" or "Select" => LabelFor(value),
            _ => value,
        };

        return string.IsNullOrEmpty(question.Suffix) ? rendered : $"{rendered}{question.Suffix}";

        string LabelFor(string optionValue) =>
            question.Options.FirstOrDefault(option => option.Value == optionValue)?.Label ?? optionValue;
    }
}