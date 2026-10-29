using Domain.MaturityAssessments;

namespace Application.MaturityAssessments.Prefill;

// The precedence rule of BLUM-7856 in one place: quarterly beats prior assessment beats baseline; a blank never
// overwrites a lower-precedence value; codes outside the catalogue are dropped; output follows catalogue order so
// the persisted rows are deterministic.
public static class PrefillMerger
{
    public static IReadOnlyList<MaturityPrefilledAnswer> Merge(
        IReadOnlyList<string> protocolCodes,
        IReadOnlyDictionary<string, string> quarterly,
        IReadOnlyDictionary<string, string> prior,
        IReadOnlyDictionary<string, string> baseline)
    {
        var result = new List<MaturityPrefilledAnswer>();

        foreach (var code in protocolCodes)
        {
            if (TryTake(quarterly, code, out var value))
            {
                result.Add(new MaturityPrefilledAnswer(code, value, AnswerSource.Quarterly));
            }
            else if (TryTake(prior, code, out value))
            {
                result.Add(new MaturityPrefilledAnswer(code, value, AnswerSource.PriorAssessment));
            }
            else if (TryTake(baseline, code, out value))
            {
                result.Add(new MaturityPrefilledAnswer(code, value, AnswerSource.Baseline2025));
            }
        }

        return result;
    }

    private static bool TryTake(IReadOnlyDictionary<string, string> source, string code, out string value)
    {
        if (source.TryGetValue(code, out var found) && !string.IsNullOrWhiteSpace(found))
        {
            value = found;
            return true;
        }

        value = string.Empty;
        return false;
    }
}