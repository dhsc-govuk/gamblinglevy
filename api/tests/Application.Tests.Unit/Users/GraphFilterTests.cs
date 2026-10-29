using FluentAssertions;
using Infrastructure.Graph;
using Xunit;

namespace Application.Tests.Unit.Users;

/// <summary>
/// BLUM-8048 — creating a user whose email contained an apostrophe returned a 500, because
/// UsersService.FindByEmail interpolated the address straight into <c>mail eq '…'</c> and Graph rejected
/// the filter ("Invalid filter clause: Syntax error at position 19"). Integration tests swap the Graph
/// service for InMemoryUsersService, which never parses a filter, so this is the only test that sees the bug.
/// </summary>
public class GraphFilterTests
{
    [Fact]
    public void ShouldEscapeTheApostropheFromTheReportedAddress()
    {
        GraphFilter.MailEquals("test.t'est@test.com").Should().Be("mail eq 'test.t''est@test.com'");
    }

    [Theory]
    [InlineData("jane.smith@council.gov.uk", "'jane.smith@council.gov.uk'")]
    [InlineData("o'brien@council.gov.uk", "'o''brien@council.gov.uk'")]
    [InlineData("o'neill.d'arcy@council.gov.uk", "'o''neill.d''arcy@council.gov.uk'")]
    [InlineData("'quoted'@council.gov.uk", "'''quoted''@council.gov.uk'")]
    [InlineData("", "''")]
    public void ShouldDoubleEverySingleQuoteInsideTheLiteral(string value, string expected)
    {
        GraphFilter.StringLiteral(value).Should().Be(expected);
    }

    [Theory]
    [InlineData("o'brien@council.gov.uk")]
    [InlineData("a''b@x.com")]
    [InlineData("x' or mail ne '@x.com")]
    public void ShouldNeverLetTheValueCloseTheLiteralEarly(string value)
    {
        var literal = GraphFilter.StringLiteral(value);

        // Well-formed means: wrapped in one quote each side, and every quote in between is part of a pair.
        literal.Should().StartWith("'").And.EndWith("'");
        literal[1..^1].Replace("''", string.Empty, StringComparison.Ordinal).Should().NotContain("'");
    }
}