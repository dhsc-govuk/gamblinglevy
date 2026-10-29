using Application.MaturityAssessments.Prefill;
using Domain.MaturityAssessments;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.MaturityAssessments.Prefill;

// Precedence quarterly → prior assessment → 2025 baseline (BLUM-7856 AC1–AC3), computed once at start. A blank
// from a higher source never masks a lower one, and only catalogue codes survive, in catalogue order.
public class PrefillMergerTests
{
    private static readonly IReadOnlyList<string> Codes = ["MA_A", "MA_B", "MA_C", "MA_D"];

    private static Dictionary<string, string> Source(params (string Code, string Value)[] values) =>
        values.ToDictionary(v => v.Code, v => v.Value, StringComparer.Ordinal);

    [Fact]
    public void Merge_TakesTheHighestPrecedenceSourcePerCode()
    {
        var merged = PrefillMerger.Merge(
            Codes,
            Source(("MA_A", "q")),
            Source(("MA_A", "p"), ("MA_B", "p")),
            Source(("MA_A", "b"), ("MA_B", "b"), ("MA_C", "b")));

        merged.Should().Equal(
            new MaturityPrefilledAnswer("MA_A", "q", AnswerSource.Quarterly),
            new MaturityPrefilledAnswer("MA_B", "p", AnswerSource.PriorAssessment),
            new MaturityPrefilledAnswer("MA_C", "b", AnswerSource.Baseline2025));
    }

    [Fact]
    public void Merge_LetsABlankFallThrough_AndDropsUnknownCodes()
    {
        var merged = PrefillMerger.Merge(
            Codes,
            Source(("MA_A", " "), ("MA_ZZ", "q")),
            Source(("MA_A", "p")),
            Source());

        merged.Should().ContainSingle().Which.Should().Be(new MaturityPrefilledAnswer("MA_A", "p", AnswerSource.PriorAssessment));
    }

    [Fact]
    public void Merge_KeepsCatalogueOrder()
    {
        var merged = PrefillMerger.Merge(Codes, Source(("MA_D", "q")), Source(("MA_A", "p")), Source(("MA_B", "b")));

        merged.Select(m => m.QuestionCode).Should().Equal("MA_A", "MA_B", "MA_D");
    }
}