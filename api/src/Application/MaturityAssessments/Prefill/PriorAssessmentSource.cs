using Application.MaturityAssessments.Protocol;
using Domain.MaturityAssessments;

namespace Application.MaturityAssessments.Prefill;

// Filters a prior assessment's (or the baseline's) stored answers down to what the CURRENT catalogue can still
// hold: unknown codes are dropped, a choice value no longer in its option list is dropped, a multi-select keeps
// only its still-known members, and a roles answer must still parse. Pure, so it is unit-tested directly.
public static class PriorAssessmentSource
{
    public static IReadOnlyDictionary<string, string> Filter(
        IEnumerable<(string QuestionCode, string Value)> rows,
        IReadOnlyList<MaturityResolvedQuestion> questions)
    {
        var byCode = questions.ToDictionary(q => q.Code, StringComparer.Ordinal);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (code, raw) in rows)
        {
            if (string.IsNullOrWhiteSpace(raw) || !byCode.TryGetValue(code, out var question) || result.ContainsKey(code))
            {
                continue;
            }

            var value = raw.Trim();
            switch (question.QuestionType)
            {
                case MaturityFieldTypes.Radio:
                case MaturityFieldTypes.Select:
                    if (question.Options.Any(o => o.Value == value))
                    {
                        result[code] = value;
                    }

                    break;

                case MaturityFieldTypes.Checkbox:
                    var kept = value
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(v => question.Options.Any(o => o.Value == v))
                        .Distinct(StringComparer.Ordinal)
                        .ToList();
                    if (kept.Count > 0)
                    {
                        result[code] = string.Join(",", kept);
                    }

                    break;

                case MaturityFieldTypes.Roles:
                    if (MaturityRolesAnswer.TryParse(value, out var entries) && entries.Count > 0)
                    {
                        result[code] = MaturityRolesAnswer.Serialize(entries);
                    }

                    break;

                default:
                    result[code] = value;
                    break;
            }
        }

        return result;
    }

    public static IEnumerable<(string QuestionCode, string Value)> Rows(IEnumerable<MaturityAssessmentAnswer> answers) =>
        answers.Select(a => (a.QuestionCode, a.Value));

    public static IEnumerable<(string QuestionCode, string Value)> Rows(IEnumerable<MaturityBaselineAnswer> answers) =>
        answers.Select(a => (a.QuestionCode, a.Value));
}