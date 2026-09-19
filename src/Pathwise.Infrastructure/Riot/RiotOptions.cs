namespace Pathwise.Infrastructure.Riot;

public sealed class RiotOptions
{
    public const string SectionName = "Riot";
    public PlayerOptions Player { get; set; } = new();
    public RoutingOptions Routing { get; set; } = new();
    public int RecentMatchCount { get; set; } = 50;
    public string ApiKey { get; set; } = string.Empty;

    public sealed class PlayerOptions { public string GameName { get; set; } = string.Empty; public string TagLine { get; set; } = string.Empty; }
    public sealed class RoutingOptions { public string Platform { get; set; } = "euw1"; public string Regional { get; set; } = "europe"; }
}
