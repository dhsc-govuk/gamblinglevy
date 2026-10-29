using Application.MaturityAssessments.Prefill;
using Application.MaturityAssessments.Protocol;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.MaturityAssessments.Prefill;

// A prior assessment or the 2025 baseline may hold answers the current catalogue no longer offers; those are
// dropped rather than surfaced as a value the LA can neither see nor change.
public class PriorAssessmentSourceTests
{
    private static readonly IReadOnlyList<MaturityResolvedQuestion> Questions = MaturityQuestionResolver.ResolveAll();

    [Fact]
    public void Filter_KeepsValidChoicesAndFreeText_DropsTheRest()
    {
        var rows = new List<(string QuestionCode, string Value)>
        {
            ("MA_DESIGNATED_LEAD", "yes"),
            ("MA_NA_PUBLISHED", "retired_value"),
            ("MA_NA_COVERAGE", "prevalence,retired,service_map"),
            ("MA_PRIORITIES", "  Reduce harm  "),
            ("MA_GONE", "yes"),
            ("MA_NA_REGIONAL", " "),
        };

        var filtered = PriorAssessmentSource.Filter(rows, Questions);

        filtered.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["MA_DESIGNATED_LEAD"] = "yes",
            ["MA_NA_COVERAGE"] = "prevalence,service_map",
            ["MA_PRIORITIES"] = "Reduce harm",
        });
    }

    [Fact]
    public void Filter_CanonicalisesRoles_AndDropsAnEmptyRepeater()
    {
        var rows = new List<(string QuestionCode, string Value)>
        {
            ("MA_FUNDED_ROLES", "[{\"role\":\"public_health_officer\",\"time\":\"81_100\",\"otherRole\":null}]"),
        };

        var filtered = PriorAssessmentSource.Filter(rows, Questions);

        MaturityRolesAnswer.TryParse(filtered["MA_FUNDED_ROLES"], out var entries).Should().BeTrue();
        entries.Should().ContainSingle();

        PriorAssessmentSource.Filter([("MA_FUNDED_ROLES", "[]")], Questions).Should().BeEmpty();
    }

    [Fact]
    public void Filter_FirstRowWinsForADuplicateCode()
    {
        var filtered = PriorAssessmentSource.Filter([("MA_DESIGNATED_LEAD", "yes"), ("MA_DESIGNATED_LEAD", "no")], Questions);

        filtered["MA_DESIGNATED_LEAD"].Should().Be("yes");
    }
}