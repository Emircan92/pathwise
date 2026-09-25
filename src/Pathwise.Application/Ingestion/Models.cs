using Pathwise.Domain.Matches;

namespace Pathwise.Application.Ingestion;

public sealed record RiotSettingsSnapshot(string GameName, string TagLine, string Platform, string Regional, int RecentMatchCount, string ApiKey)
{
    public IReadOnlyList<string> Validate(bool requireApiKey)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(GameName)) errors.Add("Riot player game name is not configured.");
        if (string.IsNullOrWhiteSpace(TagLine)) errors.Add("Riot player tag line is not configured.");
        if (!string.Equals(Platform, "euw1", StringComparison.OrdinalIgnoreCase)) errors.Add("Riot platform must be euw1 for this slice.");
        if (!string.Equals(Regional, "europe", StringComparison.OrdinalIgnoreCase)) errors.Add("Riot regional route must be europe for EUW.");
        if (RecentMatchCount is < 1 or > 100) errors.Add("Recent match count must be between 1 and 100.");
        if (requireApiKey && string.IsNullOrWhiteSpace(ApiKey)) errors.Add("Riot API key is not configured.");
        return errors;
    }
}

public sealed record RiotAccountResult(string Puuid, string GameName, string TagLine, string RawJson);
public sealed record RiotPayloadResult(bool Success, string? RawJson, RiotFailure? Failure, DateTimeOffset? RetryAfterUtc = null);
public sealed record RiotFailure(string Code, string Message, int? HttpStatus = null, bool StopOperation = false);
public enum PayloadKind { Match, Timeline }
public enum PayloadState { Missing, Stored, Failed, Invalid }
public sealed record StoredPayloadState(PayloadKind Kind, PayloadState State, string? RawJson);
public sealed record StoredMatchState(string MatchId, bool HasMetadata, IReadOnlyDictionary<PayloadKind, StoredPayloadState> Payloads)
{
    public bool IsComplete => HasMetadata && Payloads[PayloadKind.Match].State == PayloadState.Stored && Payloads[PayloadKind.Timeline].State == PayloadState.Stored;
}

public sealed record PlayerView(string GameName, string TagLine, string Platform, string Regional, int FetchCount, bool FetchReady, IReadOnlyList<string> ConfigurationErrors, string? Puuid, DateTimeOffset? ResolvedAtUtc);
public sealed record MatchFailureView(string Resource, string Code, string Message, int? HttpStatus, DateTimeOffset? RetryAfterUtc);
public sealed record MatchView(string MatchId, int? QueueId, DateTimeOffset? PlayedAtUtc, int? DurationSeconds, string? ChampionName, bool? Won, string? TeamPosition, string SupportStatus, string IngestionStatus, bool MatchPayloadAvailable, bool TimelinePayloadAvailable, IReadOnlyList<MatchFailureView> Failures);
public sealed record MatchListView(int TotalStored, int Limit, int Offset, IReadOnlyList<MatchView> Matches, IReadOnlyList<MatchView> IncompleteImports);
public sealed record FetchError(string? MatchId, string Resource, string Code, string Message, int? HttpStatus);
public sealed record FetchResult(string Outcome, int RequestedCount, int DiscoveredCount, IReadOnlyList<string> NewlyCompleted, IReadOnlyList<string> Repaired, IReadOnlyList<string> AlreadyComplete, IReadOnlyList<string> Incomplete, IReadOnlyList<string> Deferred, IReadOnlyList<FetchError> Errors, DateTimeOffset? RetryAfterUtc);

public sealed class FetchConflictException : Exception;
public sealed class FetchConfigurationException(IReadOnlyList<string> errors) : Exception(string.Join(" ", errors)) { public IReadOnlyList<string> Errors { get; } = errors; }
public sealed class RiotOperationException(RiotFailure failure, DateTimeOffset? retryAfterUtc = null) : Exception(failure.Message)
{
    public RiotFailure Failure { get; } = failure;
    public DateTimeOffset? RetryAfterUtc { get; } = retryAfterUtc;
}

public interface IRiotSource
{
    Task<RiotAccountResult> ResolveAccountAsync(RiotSettingsSnapshot settings, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetRecentRankedMatchIdsAsync(string puuid, RiotSettingsSnapshot settings, CancellationToken cancellationToken);
    Task<RiotPayloadResult> GetPayloadAsync(string matchId, PayloadKind kind, RiotSettingsSnapshot settings, CancellationToken cancellationToken);
    MatchFacts ProjectMatch(string rawJson, string expectedMatchId, string playerPuuid);
}

public interface IMatchStore
{
    Task<PlayerView?> GetResolvedPlayerAsync(RiotSettingsSnapshot settings, CancellationToken cancellationToken);
    Task SaveResolvedPlayerAsync(RiotAccountResult account, RiotSettingsSnapshot settings, DateTimeOffset resolvedAtUtc, CancellationToken cancellationToken);
    Task EnsureMatchesAsync(string puuid, string regional, IReadOnlyList<string> matchIds, DateTimeOffset discoveredAtUtc, CancellationToken cancellationToken);
    Task<StoredMatchState> GetStateAsync(string matchId, CancellationToken cancellationToken);
    Task SavePayloadAsync(string matchId, PayloadKind kind, RiotPayloadResult result, DateTimeOffset attemptedAtUtc, CancellationToken cancellationToken);
    Task SaveMetadataAsync(string matchId, MatchFacts facts, CancellationToken cancellationToken);
    Task SaveMetadataFailureAsync(string matchId, string code, string message, DateTimeOffset failedAtUtc, CancellationToken cancellationToken);
    Task<MatchListView> GetMatchesAsync(string? puuid, int limit, int offset, CancellationToken cancellationToken);
    Task<MatchView?> GetMatchAsync(string? puuid, string matchId, CancellationToken cancellationToken);
}
