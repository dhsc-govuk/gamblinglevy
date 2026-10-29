using System.Text.RegularExpressions;
using Application.Common.Features;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.Features;

/// <summary>
/// The flag registry's naming rule: every member is the Jira ticket that introduced it, so a flag seen in an
/// environment variable, a boot log or the client's profile traces straight back to its ticket.
/// </summary>
public partial class FeatureTests
{
    [Fact]
    public void ShouldNameEveryFlagAfterItsTicket()
    {
        Enum.GetNames<Feature>().Should().OnlyContain(
            name => TicketName().IsMatch(name),
            "a flag is named BLUM_{ticket number} (BLUM_7637 for BLUM-7637), never after what it does");
    }

    [GeneratedRegex("^BLUM_[1-9][0-9]*$")]
    private static partial Regex TicketName();
}