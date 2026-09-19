using Pathwise.Application.Ingestion;
using Pathwise.Application.Reconstruction;
using Pathwise.Domain.Matches;
using Pathwise.Domain.Reconstruction;

namespace Pathwise.Application.Tests;

public sealed class ReconstructionServiceTests
{
    [Fact]
    public async Task ChangesLoadsSourceOnceAndDoesNotRequireApiKey()
    {
        var store = new FakeStore(new PlayerView("Player", "EUW", "euw1", "europe", 50, true, [], "local-puuid", DateTimeOffset.UtcNow));
        var source = new FakeSource();
        var service = new ReconstructionService(store, source);

        var result = await service.GetChangesAsync(new("Player", "EUW", "euw1", "europe", 50, ""), "EUW1_1", 0, 100, CancellationToken.None);

        Assert.Equal(1, source.LoadCount);
        Assert.Equal("local-puuid", source.LastPuuid);
        Assert.Equal(100, result.Value.ConfiguredPlayerDelta.TotalGold);
    }

    [Fact]
    public async Task MissingLocalAccountReturnsMatchUnavailableWithoutLoadingSource()
    {
        var source = new FakeSource();
        var service = new ReconstructionService(new FakeStore(null), source);

        var exception = await Assert.ThrowsAsync<ReconstructionRequestException>(() =>
            service.GetMetadataAsync(new("Player", "EUW", "euw1", "europe", 50, ""), "EUW1_1", CancellationToken.None));

        Assert.Equal(ReconstructionFailureKind.MatchUnavailable, exception.Failure.Kind);
        Assert.Equal(0, source.LoadCount);
    }

    private sealed class FakeSource : IStoredReconstructionSource
    {
        public int LoadCount { get; private set; }
        public string? LastPuuid { get; private set; }

        public Task<StoredReconstructionLoadResult> LoadAsync(string matchId, string configuredPlayerPuuid, CancellationToken cancellationToken)
        {
            LoadCount++;
            LastPuuid = configuredPlayerPuuid;
            var participant = new Participant(2, 100, 121, "Khazix", "JUNGLE", false, 0, 0, 0);
            var frames = new[]
            {
                new FrameObservation(0, 0, Observation(1000), null),
                new FrameObservation(1, 100, Observation(1100), null)
            };
            var input = new ReconstructionInput(matchId, "1.0", 420, 11, 1, new[] { participant }, 2, new(EnemyResolutionStatus.Missing, null), frames, Array.Empty<ReconstructionEvent>(), Array.Empty<SourceDataIssue>());
            var metadata = new ReconstructionSourceMetadata(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "v5", "v5");
            return Task.FromResult(StoredReconstructionLoadResult.Success(new(input, metadata)));
        }

        private static PlayerObservation Observation(long gold) => new(2, null, gold, 0, 0, 1, 0, 0, null, null, null, null);
    }

    private sealed class FakeStore(PlayerView? player) : IMatchStore
    {
        public Task<PlayerView?> GetResolvedPlayerAsync(RiotSettingsSnapshot settings, CancellationToken cancellationToken) => Task.FromResult(player);
        public Task SaveResolvedPlayerAsync(RiotAccountResult account, RiotSettingsSnapshot settings, DateTimeOffset resolvedAtUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task EnsureMatchesAsync(string puuid, string regional, IReadOnlyList<string> matchIds, DateTimeOffset discoveredAtUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StoredMatchState> GetStateAsync(string matchId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SavePayloadAsync(string matchId, PayloadKind kind, RiotPayloadResult result, DateTimeOffset attemptedAtUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveMetadataAsync(string matchId, MatchFacts facts, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveMetadataFailureAsync(string matchId, string code, string message, DateTimeOffset failedAtUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MatchListView> GetMatchesAsync(string? puuid, int limit, int offset, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MatchView?> GetMatchAsync(string? puuid, string matchId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
