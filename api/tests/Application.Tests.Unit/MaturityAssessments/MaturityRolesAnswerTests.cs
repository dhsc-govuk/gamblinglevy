using Application.MaturityAssessments.Protocol;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.MaturityAssessments;

public class MaturityRolesAnswerTests
{
    [Fact]
    public void Serialize_ThenTryParse_RoundTrips()
    {
        var entries = new List<MaturityRolesAnswer.Entry>
        {
            new("public_health_officer", "81_100", null),
            new("other", "21_40", "Data analyst"),
        };

        var json = MaturityRolesAnswer.Serialize(entries);
        MaturityRolesAnswer.TryParse(json, out var parsed).Should().BeTrue();

        parsed.Should().Equal(entries);
    }

    [Fact]
    public void TryParse_TreatsBlankAsNoRoles_AndRefusesGarbage()
    {
        MaturityRolesAnswer.TryParse(null, out var none).Should().BeTrue();
        none.Should().BeEmpty();

        MaturityRolesAnswer.TryParse("{\"role\":\"x\"}", out _).Should().BeFalse();
        MaturityRolesAnswer.TryParse("nope", out _).Should().BeFalse();
    }

    [Fact]
    public void Render_NamesEveryRoleWithItsTimeBand()
    {
        var entries = new List<MaturityRolesAnswer.Entry>
        {
            new("public_health_officer", "81_100", null),
            new("other", "41_60", "Data analyst"),
        };

        var rendered = MaturityRolesAnswer.Render(entries, MaturityProtocol.FundedRoleOptions, MaturityProtocol.FundedTimeOptions, MaturityOptionValues.None);

        rendered.Should().Contain("Public Health Officer").And.Contain("81% to 100%");
        rendered.Should().Contain("Data analyst").And.Contain("41% to 60%");
    }

    // BLUM-9112: the exclusive "None" is one row with no time, rendered as the bare answer rather than "Role 1: None ()".
    [Fact]
    public void Render_ShowsTheExclusiveAnswerOnItsOwn()
    {
        var entries = new List<MaturityRolesAnswer.Entry> { new("none", string.Empty, null) };

        MaturityRolesAnswer.IsExclusiveAnswer(entries, MaturityOptionValues.None).Should().BeTrue();
        MaturityRolesAnswer.Render(entries, MaturityProtocol.FundedRoleOptions, MaturityProtocol.FundedTimeOptions, MaturityOptionValues.None).Should().Be("None");
    }

    [Fact]
    public void IsExclusiveAnswer_IsFalseForRealRoles_NoAnswer_AndAQuestionWithoutAnExclusiveValue()
    {
        MaturityRolesAnswer.IsExclusiveAnswer([], MaturityOptionValues.None).Should().BeFalse();
        MaturityRolesAnswer.IsExclusiveAnswer([new("public_health_officer", "81_100", null)], MaturityOptionValues.None).Should().BeFalse();
        MaturityRolesAnswer.IsExclusiveAnswer([new("none", string.Empty, null), new("public_health_officer", "81_100", null)], MaturityOptionValues.None).Should().BeFalse();
        MaturityRolesAnswer.IsExclusiveAnswer([new("none", string.Empty, null)], null).Should().BeFalse();
    }

    [Fact]
    public void Canonical_StripsOnlyTheExclusiveRow()
    {
        var entries = new List<MaturityRolesAnswer.Entry> { new("none", "21_40", "Data analyst") };

        MaturityRolesAnswer.Canonical(entries, MaturityOptionValues.None).Should().Equal(new MaturityRolesAnswer.Entry("none", string.Empty, null));
        MaturityRolesAnswer.Canonical(entries, null).Should().Equal(entries);
    }
}