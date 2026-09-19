namespace Pathwise.Domain.Matches;

public sealed record MatchFacts(int QueueId, DateTimeOffset PlayedAtUtc, int DurationSeconds, string ChampionName, bool Won, string? TeamPosition);

public static class MatchSupport
{
    private static readonly HashSet<string> KnownPositions = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"];

    public static string FromTeamPosition(string? value)
    {
        if (string.Equals(value, "JUNGLE", StringComparison.OrdinalIgnoreCase)) return "jungle";
        return value is not null && KnownPositions.Contains(value.ToUpperInvariant()) ? "unsupported" : "unknown";
    }
}
