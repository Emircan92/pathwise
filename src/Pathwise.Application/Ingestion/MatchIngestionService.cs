namespace Pathwise.Application.Ingestion;

public sealed class MatchIngestionService(IRiotSource riot, IMatchStore store, TimeProvider timeProvider)
{
    private static readonly SemaphoreSlim IngestionGate = new(1, 1);

    public async Task<PlayerView> GetPlayerAsync(RiotSettingsSnapshot settings, CancellationToken cancellationToken)
    {
        var cached = await store.GetResolvedPlayerAsync(settings, cancellationToken);
        var errors = settings.Validate(requireApiKey: true);
        return cached is null
            ? new(settings.GameName, settings.TagLine, settings.Platform, settings.Regional, settings.RecentMatchCount, errors.Count == 0, errors, null, null)
            : cached with { FetchReady = errors.Count == 0, ConfigurationErrors = errors };
    }

    public async Task<MatchListView> GetMatchesAsync(RiotSettingsSnapshot settings, int limit, int offset, CancellationToken cancellationToken)
    {
        var player = await store.GetResolvedPlayerAsync(settings, cancellationToken);
        return await store.GetMatchesAsync(player?.Puuid, limit, offset, cancellationToken);
    }

    public async Task<MatchView?> GetMatchAsync(RiotSettingsSnapshot settings, string matchId, CancellationToken cancellationToken)
    {
        var player = await store.GetResolvedPlayerAsync(settings, cancellationToken);
        return await store.GetMatchAsync(player?.Puuid, matchId, cancellationToken);
    }

    public async Task<FetchResult> FetchAsync(RiotSettingsSnapshot settings, CancellationToken cancellationToken)
    {
        var configErrors = settings.Validate(requireApiKey: true);
        if (configErrors.Count > 0) throw new FetchConfigurationException(configErrors);
        if (!await IngestionGate.WaitAsync(0, cancellationToken)) throw new FetchConflictException();

        try
        {
            var account = await riot.ResolveAccountAsync(settings, cancellationToken);
            await store.SaveResolvedPlayerAsync(account, settings, timeProvider.GetUtcNow(), cancellationToken);
            var ids = (await riot.GetRecentRankedMatchIdsAsync(account.Puuid, settings, cancellationToken)).Distinct().ToArray();
            await store.EnsureMatchesAsync(account.Puuid, settings.Regional, ids, timeProvider.GetUtcNow(), cancellationToken);

            var newlyCompleted = new List<string>();
            var repaired = new List<string>();
            var alreadyComplete = new List<string>();
            var incomplete = new List<string>();
            var deferred = new List<string>();
            var errors = new List<FetchError>();
            DateTimeOffset? retryAfter = null;
            var stop = false;

            foreach (var id in ids)
            {
                var initial = await store.GetStateAsync(id, cancellationToken);
                if (initial.IsComplete) { alreadyComplete.Add(id); continue; }
                var hadStoredData = initial.Payloads.Values.Any(x => x.State == PayloadState.Stored);

                foreach (var kind in new[] { PayloadKind.Match, PayloadKind.Timeline })
                {
                    if (stop) break;
                    var current = await store.GetStateAsync(id, cancellationToken);
                    if (current.Payloads[kind].State == PayloadState.Stored) continue;
                    var result = await riot.GetPayloadAsync(id, kind, settings, cancellationToken);
                    await store.SavePayloadAsync(id, kind, result, timeProvider.GetUtcNow(), cancellationToken);
                    if (!result.Success && result.Failure is not null)
                    {
                        errors.Add(new(id, kind.ToString().ToLowerInvariant(), result.Failure.Code, result.Failure.Message, result.Failure.HttpStatus));
                        retryAfter = result.RetryAfterUtc ?? retryAfter;
                        stop = result.Failure.StopOperation;
                    }
                }

                var state = await store.GetStateAsync(id, cancellationToken);
                if (state.Payloads[PayloadKind.Match].State == PayloadState.Stored && !state.HasMetadata)
                {
                    try
                    {
                        await store.SaveMetadataAsync(id, riot.ProjectMatch(state.Payloads[PayloadKind.Match].RawJson!, id, account.Puuid), cancellationToken);
                    }
                    catch (Exception ex) when (ex is FormatException or InvalidOperationException)
                    {
                        await store.SaveMetadataFailureAsync(id, "invalid_match_metadata", ex.Message, timeProvider.GetUtcNow(), cancellationToken);
                        errors.Add(new(id, "metadata", "invalid_match_metadata", ex.Message, null));
                    }
                }

                var final = await store.GetStateAsync(id, cancellationToken);
                if (final.IsComplete) (hadStoredData ? repaired : newlyCompleted).Add(id); else incomplete.Add(id);
                if (stop) break;
            }

            if (stop)
            {
                var processed = newlyCompleted.Concat(repaired).Concat(alreadyComplete).Concat(incomplete).ToHashSet();
                deferred.AddRange(ids.Where(x => !processed.Contains(x)));
            }

            var useful = newlyCompleted.Count + repaired.Count + alreadyComplete.Count;
            var outcome = errors.Count == 0 && incomplete.Count == 0 && deferred.Count == 0 ? "succeeded" : useful > 0 ? "partial" : "failed";
            return new(outcome, settings.RecentMatchCount, ids.Length, newlyCompleted, repaired, alreadyComplete, incomplete, deferred, errors, retryAfter);
        }
        finally { IngestionGate.Release(); }
    }
}
