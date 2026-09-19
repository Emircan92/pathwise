using Pathwise.Application.Ingestion;

namespace Pathwise.Infrastructure.Persistence;

public sealed class PlayerAccountEntity
{
    public required string Puuid { get; set; }
    public required string GameName { get; set; }
    public required string TagLine { get; set; }
    public required string ConfiguredGameName { get; set; }
    public required string ConfiguredTagLine { get; set; }
    public required string Platform { get; set; }
    public required string Regional { get; set; }
    public DateTimeOffset ResolvedAtUtc { get; set; }
    public required string RawJson { get; set; }
    public List<StoredMatchEntity> Matches { get; set; } = [];
}

public sealed class StoredMatchEntity
{
    public required string MatchId { get; set; }
    public required string PlayerPuuid { get; set; }
    public required string Regional { get; set; }
    public DateTimeOffset DiscoveredAtUtc { get; set; }
    public int? QueueId { get; set; }
    public DateTimeOffset? PlayedAtUtc { get; set; }
    public int? DurationSeconds { get; set; }
    public string? ChampionName { get; set; }
    public bool? Won { get; set; }
    public string? TeamPosition { get; set; }
    public string? MetadataErrorCode { get; set; }
    public string? MetadataErrorMessage { get; set; }
    public DateTimeOffset? MetadataErrorAtUtc { get; set; }
    public PlayerAccountEntity Player { get; set; } = null!;
    public List<MatchPayloadEntity> Payloads { get; set; } = [];
}

public sealed class MatchPayloadEntity
{
    public required string MatchId { get; set; }
    public PayloadKind Kind { get; set; }
    public string? RawJson { get; set; }
    public DateTimeOffset? RetrievedAtUtc { get; set; }
    public string EndpointVersion { get; set; } = "v5";
    public PayloadState State { get; set; }
    public DateTimeOffset? LastAttemptAtUtc { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public int? FailureHttpStatus { get; set; }
    public DateTimeOffset? RetryAfterUtc { get; set; }
    public StoredMatchEntity Match { get; set; } = null!;
}
