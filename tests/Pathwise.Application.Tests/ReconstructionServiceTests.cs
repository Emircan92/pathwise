using Pathwise.Application.Ingestion;
using Pathwise.Application.Reconstruction;
using Pathwise.Application.ReviewWindows;
using Pathwise.Domain.FactualObservations;
using Pathwise.Domain.Matches;
using Pathwise.Domain.Reconstruction;
using Pathwise.Domain.ReviewWindows;

namespace Pathwise.Application.Tests;

public sealed class ReconstructionServiceTests
{
    [Fact]
    public async Task ReviewWindowsLoadOneLocalSourceWithoutApiKeyOrWrites()
    {
        var store = new FakeStore(new PlayerView("Player", "EUW", "euw1", "europe", 50, true, [], "local-puuid", DateTimeOffset.UtcNow));
        var source = new FakeSource();
        var service = new ReviewWindowService(new ReconstructionService(store, source));

        var result = await service.GetAsync(new("Player", "EUW", "euw1", "europe", 50, ""), "EUW1_1", CancellationToken.None);

        Assert.Equal(1, source.LoadCount);
        Assert.Equal("local-puuid", source.LastPuuid);
        Assert.Empty(result.Value.Candidates);
        Assert.Equal(result.Reconstruction.ReconstructionVersion, result.Value.ReconstructionVersion);
        Assert.Equal(DateTimeOffset.UnixEpoch, result.Source.MatchRetrievedAtUtc);
    }

    [Fact]
    public async Task ReviewWindowsPreserveReconstructionFailures()
    {
        var store = new FakeStore(new PlayerView("Player", "EUW", "euw1", "europe", 50, true, [], "local-puuid", DateTimeOffset.UtcNow));
        var failure = new ReconstructionFailure(ReconstructionFailureKind.InvalidSource, "invalid_source", "Invalid source.");
        var service = new ReviewWindowService(new ReconstructionService(store, new FakeSource(failure)));

        var exception = await Assert.ThrowsAsync<ReconstructionRequestException>(() =>
            service.GetAsync(new("Player", "EUW", "euw1", "europe", 50, ""), "EUW1_1", CancellationToken.None));

        Assert.Same(failure, exception.Failure);
    }

    [Fact]
    public async Task MatchReviewComposesOneReviewWindowLoadAndPreservesMetadataAndDetection()
    {
        var store = new FakeStore(new PlayerView("Player", "EUW", "euw1", "europe", 50, true, [], "local-puuid", DateTimeOffset.UtcNow));
        var source = new FakeSource(withReviewWindow: true);
        var reviewWindowService = new ReviewWindowService(new ReconstructionService(store, source));
        var service = new MatchReviewService(reviewWindowService);

        var result = await service.GetAsync(
            new("Player", "EUW", "euw1", "europe", 50, ""),
            "EUW1_1",
            CancellationToken.None);

        Assert.Equal(1, source.LoadCount);
        Assert.Equal("local-puuid", source.LastPuuid);
        Assert.Equal(DateTimeOffset.UnixEpoch, result.Source.MatchRetrievedAtUtc);
        Assert.Equal(ReviewWindowDetector.CurrentVersion, result.Value.WindowDetection.DetectorVersion);
        var candidate = Assert.Single(result.Value.WindowDetection.Candidates);
        Assert.Equal((0L, 180_000L),
            (candidate.RequestedStartTimestampMs, candidate.RequestedEndTimestampMs));
        Assert.Equal(ReviewSelectionReason.GoldChange, candidate.PrimarySelectionReason);
        Assert.Equal(ReviewWindowOptions.Default, result.Value.WindowDetection.EffectiveOptions);
        Assert.Equal(FactualObservationGenerator.CurrentVersion, result.Value.FactualObservations.GeneratorVersion);
        var factualWindow = Assert.Single(result.Value.FactualObservations.Windows);
        Assert.IsType<RelativeGoldObservation>(Assert.Single(factualWindow.Observations));
    }

    [Fact]
    public async Task MatchReviewPreservesFailuresAndCancellation()
    {
        var store = new FakeStore(new PlayerView("Player", "EUW", "euw1", "europe", 50, true, [], "local-puuid", DateTimeOffset.UtcNow));
        var failure = new ReconstructionFailure(ReconstructionFailureKind.InvalidSource, "invalid_source", "Invalid source.");
        var failedService = new MatchReviewService(new ReviewWindowService(
            new ReconstructionService(store, new FakeSource(failure))));

        var exception = await Assert.ThrowsAsync<ReconstructionRequestException>(() =>
            failedService.GetAsync(new("Player", "EUW", "euw1", "europe", 50, ""), "EUW1_1", CancellationToken.None));
        Assert.Same(failure, exception.Failure);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelledService = new MatchReviewService(new ReviewWindowService(
            new ReconstructionService(store, new FakeSource())));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cancelledService.GetAsync(new("Player", "EUW", "euw1", "europe", 50, ""), "EUW1_1", cancellation.Token));
    }

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

    private sealed class FakeSource(
        ReconstructionFailure? failure = null,
        bool withReviewWindow = false) : IStoredReconstructionSource
    {
        public int LoadCount { get; private set; }
        public string? LastPuuid { get; private set; }

        public Task<StoredReconstructionLoadResult> LoadAsync(string matchId, string configuredPlayerPuuid, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadCount++;
            LastPuuid = configuredPlayerPuuid;
            if (failure is not null)
                return Task.FromResult(StoredReconstructionLoadResult.Failed(failure));
            var participant = new Participant(2, 100, 121, "Khazix", "JUNGLE", false, 0, 0, 0);
            var participants = withReviewWindow
                ? new[] { participant, new Participant(7, 200, 200, "Belveth", "JUNGLE", true, 0, 0, 0) }
                : [participant];
            var frames = withReviewWindow
                ? new[]
                {
                    new FrameObservation(0, 0, Observation(2, 1000), Observation(7, 1000)),
                    new FrameObservation(1, 90_000, Observation(2, 1000), Observation(7, 1000)),
                    new FrameObservation(2, 180_000, Observation(2, 2000), Observation(7, 1000))
                }
                :
                [
                    new FrameObservation(0, 0, Observation(2, 1000), null),
                    new FrameObservation(1, 100, Observation(2, 1100), null)
                ];
            var enemyResolution = withReviewWindow
                ? new EnemyJunglerResolution(EnemyResolutionStatus.Resolved, 7)
                : new EnemyJunglerResolution(EnemyResolutionStatus.Missing, null);
            var input = new ReconstructionInput(matchId, "1.0", 420, 11, 1, participants, 2, enemyResolution, frames, Array.Empty<ReconstructionEvent>(), Array.Empty<SourceDataIssue>());
            var metadata = new ReconstructionSourceMetadata(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "v5", "v5");
            return Task.FromResult(StoredReconstructionLoadResult.Success(new(input, metadata)));
        }

        private static PlayerObservation Observation(int participantId, long gold) =>
            new(participantId, null, gold, 0, 0, 1, 0, 0, null, null, null, null);
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
