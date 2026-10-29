using FluentAssertions;
using Infrastructure.Persistence.Seed.MaturityBaseline;
using Xunit;

namespace Application.Tests.Unit.Seed;

public class LocalAuthorityNameMatcherTests
{
    [Theory]
    [InlineData("Barking and Dagenham London Borough", "barking and dagenham")]
    [InlineData("London Borough of Barking and Dagenham", "barking and dagenham")]
    [InlineData("Bradford Metropolitan District Council (City of)", "bradford")]
    [InlineData("Bristol, City of", "bristol")]
    [InlineData("Bristol City Council", "bristol")]
    [InlineData("Cornwall and Isles of Scilly;", "cornwall")]
    [InlineData("Telford and Wrekin Co-operative Council", "telford and wrekin")]
    [InlineData("Kensington and Chelsea Royal Borough", "kensington and chelsea")]
    [InlineData("Royal Borough of Kensington and Chelsea", "kensington and chelsea")]
    [InlineData("Kingston upon Hull, City of", "hull")]
    [InlineData("Hull City Council", "hull")]
    [InlineData("Herefordshire, County of", "herefordshire")]
    [InlineData("County Durham", "durham")]
    [InlineData("Durham County Council", "durham")]
    [InlineData("St. Helens Borough Council", "st helens")]
    [InlineData("Bournemouth, Christchurch and Poole Council ", "bournemouth christchurch and poole")]
    [InlineData("Stockton-on-Tees Borough Council", "stockton on tees")]
    [InlineData("Bath & North East Somerset Council", "bath and north east somerset")]
    [InlineData("City of London Corporation", "london")]
    public void Normalise_ReducesSurveyAndOnsSpellingsToOneKey(string name, string expected)
    {
        LocalAuthorityNameMatcher.Normalise(name).Should().Be(expected);
    }

    [Fact]
    public void Match_PairsRows_FlagsRegionMismatch_AndReportsTheRest()
    {
        var rows = new List<MaturityBaselineRow>
        {
            Row(2, "Peterborough City Council", "East of England"),
            Row(3, "Luton Borough Council", "London"),
            Row(4, "Nowhere Council", "London"),
            Row(5, "Twin Council", "London"),
            Row(6, "", "London"),
            Row(7, "City of Peterborough", "East of England"),
        };
        var luton = Guid.NewGuid();
        var peterborough = Guid.NewGuid();
        var organisations = new List<LocalAuthorityCandidate>
        {
            new(peterborough, "Peterborough", "East of England"),
            new(luton, "Luton", "East of England"),
            new(Guid.NewGuid(), "Twin", "London"),
            new(Guid.NewGuid(), "Twin Borough Council", "London"),
        };

        var result = LocalAuthorityNameMatcher.Match(rows, organisations);

        result.Matched.Should().HaveCount(2);
        result.Matched.Single(m => m.Row.RowNumber == 2).Should().Match<MatchedBaselineRow>(m => m.OrganisationId == peterborough && !m.RegionMismatch);
        result.Matched.Single(m => m.Row.RowNumber == 3).Should().Match<MatchedBaselineRow>(m => m.OrganisationId == luton && m.RegionMismatch);
        result.Unmatched.Should().BeEquivalentTo(
        [
            new { Row = rows[2], Reason = LocalAuthorityNameMatcher.NoOrganisation },
            new { Row = rows[3], Reason = LocalAuthorityNameMatcher.Ambiguous },
        ]);
        result.Skipped.Should().BeEquivalentTo(
        [
            new { Row = rows[4], Reason = LocalAuthorityNameMatcher.BlankName },
            new { Row = rows[5], Reason = LocalAuthorityNameMatcher.DuplicateRow },
        ]);
    }

    private static MaturityBaselineRow Row(int number, string name, string region) =>
        new(number, number.ToString(), region, name, new Dictionary<string, string>(StringComparer.Ordinal));
}