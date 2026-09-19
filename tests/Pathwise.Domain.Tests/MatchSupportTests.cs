using Pathwise.Domain.Matches;

namespace Pathwise.Domain.Tests;

public sealed class MatchSupportTests
{
    [Theory]
    [InlineData("JUNGLE", "jungle")]
    [InlineData("MIDDLE", "unsupported")]
    [InlineData("TOP", "unsupported")]
    [InlineData("", "unknown")]
    [InlineData(null, "unknown")]
    [InlineData("INVALID", "unknown")]
    public void UsesOnlyRiotTeamPosition(string? teamPosition, string expected)
    {
        Assert.Equal(expected, MatchSupport.FromTeamPosition(teamPosition));
    }
}
