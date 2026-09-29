using Pathwise.Application.Ingestion;
using Pathwise.Application.Interpretation;
using Pathwise.Application.Reconstruction;
using Pathwise.Application.ReviewWindows;
using Pathwise.Domain.FactualObservations;
using Pathwise.Domain.Knowledge;
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
        Assert.Equal(KnowledgeCoverage.UnknownPatch, result.Value.KnowledgeAnnotations.Coverage);
        Assert.Equal("1.0", result.Value.KnowledgeAnnotations.PatchResolution.RawGameVersion);
        Assert.Empty(result.Value.KnowledgeAnnotations.Annotations);
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

    [Fact]
    public async Task NarrativeInterpretationProjectsStableSelectedWindowEvidenceWithoutSpatialSamples()
    {
        var store = new FakeStore(new PlayerView("Player", "EUW", "euw1", "europe", 50, true, [], "local-puuid", DateTimeOffset.UtcNow));
        var source = new FakeSource(withReviewWindow: true);
        var provider = new FakeNarrativeProvider();
        var service = new NarrativeInterpretationService(
            new MatchReviewService(new ReviewWindowService(new ReconstructionService(store, source))),
            provider);
        var selection = new NarrativeInterpretationSelection(0, 180_000, 1, ReviewWindowDetector.CurrentVersion);

        var first = await service.InterpretAsync(new("Player", "EUW", "euw1", "europe", 50, ""), "EUW1_1", selection, CancellationToken.None);
        var second = await service.InterpretAsync(new("Player", "EUW", "euw1", "europe", 50, ""), "EUW1_1", selection, CancellationToken.None);

        Assert.Equal(2, provider.CallCount);
        Assert.Equal(first.InputFingerprint, second.InputFingerprint);
        var input = provider.Inputs[0];
        Assert.Equal(input.InputFingerprint, NarrativeInterpretationFingerprint.Compute(input));
        Assert.Equal(["window:selection", "observation:relativeGoldMovement"], input.Evidence.Select(value => value.Id).ToArray());
        var metric = Assert.IsType<NarrativeMetricObservationEvidenceV1>(input.Evidence[1]);
        Assert.Equal((0, 0L, 2, 180_000L), (metric.StartFrame.FrameIndex, metric.StartFrame.TimestampMs, metric.EndFrame.FrameIndex, metric.EndFrame.TimestampMs));
        Assert.Equal((0L, 1_000L, 1_000L), (metric.RelativeStart, metric.RelativeEnd, metric.SignedChange));
        var json = System.Text.Json.JsonSerializer.Serialize(input, NarrativeInterpretationJson.Options);
        Assert.DoesNotContain("configuredPlayerPosition", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("enemyJunglerPosition", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"x\":", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"y\":", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("puuid", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawJson", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(input.EvidenceLimitations, value => value.Contains("no periodic position samples", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NarrativeInterpretationPreservesMetricProvenanceBeforeRequestedWindowStart()
    {
        var store = new FakeStore(new PlayerView("Player", "EUW", "euw1", "europe", 50, true, [], "local-puuid", DateTimeOffset.UtcNow));
        var provider = new FakeNarrativeProvider();
        var service = new NarrativeInterpretationService(
            new MatchReviewService(new ReviewWindowService(new ReconstructionService(
                store,
                new FakeSource(withTemporalContractWindow: true)))),
            provider);

        await service.InterpretAsync(
            new("Player", "EUW", "euw1", "europe", 50, ""),
            "EUW1_1",
            new(1_910_458, 2_100_622, 1, ReviewWindowDetector.CurrentVersion),
            CancellationToken.None);

        var input = Assert.Single(provider.Inputs);
        Assert.Equal((1_910_458L, 2_100_622L),
            (input.Window.RequestedStartTimestampMs, input.Window.RequestedEndTimestampMs));
        Assert.Equal((31, 1_860_547L),
            (input.Window.StartFrame.FrameIndex, input.Window.StartFrame.TimestampMs));
        var metric = Assert.Single(input.Evidence.OfType<NarrativeMetricObservationEvidenceV1>());
        Assert.Equal((31, 1_860_547L, 35, 2_100_622L),
            (metric.StartFrame.FrameIndex, metric.StartFrame.TimestampMs, metric.EndFrame.FrameIndex, metric.EndFrame.TimestampMs));
        Assert.True(metric.StartFrame.TimestampMs < input.Window.RequestedStartTimestampMs);
    }

    [Fact]
    public async Task NarrativeInterpretationProjectsMetricOmissionsAndSourceIssues()
    {
        var store = new FakeStore(new PlayerView("Player", "EUW", "euw1", "europe", 50, true, [], "local-puuid", DateTimeOffset.UtcNow));
        var provider = new FakeNarrativeProvider();
        var service = new NarrativeInterpretationService(
            new MatchReviewService(new ReviewWindowService(new ReconstructionService(
                store,
                new FakeSource(withReviewWindow: true, withCounterRegressionAndIssue: true)))),
            provider);

        await service.InterpretAsync(
            new("Player", "EUW", "euw1", "europe", 50, ""),
            "EUW1_1",
            new(0, 180_000, 1, ReviewWindowDetector.CurrentVersion),
            CancellationToken.None);

        var input = Assert.Single(provider.Inputs);
        var omission = Assert.Single(input.Evidence.OfType<NarrativeMetricOmissionEvidenceV1>());
        Assert.Equal(("relativeJungleCsMovement", "counterRegression", 2), (omission.Metric, omission.Reason, omission.ParticipantId));
        Assert.Equal((10L, 4L), (omission.PreviousValue, omission.CurrentValue));
        var issue = Assert.Single(input.Evidence.OfType<NarrativeSourceIssueEvidenceV1>());
        Assert.Equal(("test_source_gap", "timeline.frames[1]", "issue:0:test_source_gap"),
            (issue.Code, issue.SourceReference, issue.Id));
    }

    [Theory]
    [InlineData(0, 180000, 99, 2, NarrativeInterpretationRequestFailureKind.StaleWindow)]
    [InlineData(1, 180000, 1, 2, NarrativeInterpretationRequestFailureKind.UnknownWindow)]
    public async Task NarrativeInterpretationRejectsStaleOrUnknownWindowBeforeProviderCall(
        long start,
        long end,
        int reconstructionVersion,
        int detectorVersion,
        NarrativeInterpretationRequestFailureKind expected)
    {
        var store = new FakeStore(new PlayerView("Player", "EUW", "euw1", "europe", 50, true, [], "local-puuid", DateTimeOffset.UtcNow));
        var provider = new FakeNarrativeProvider();
        var service = new NarrativeInterpretationService(
            new MatchReviewService(new ReviewWindowService(new ReconstructionService(store, new FakeSource(withReviewWindow: true)))),
            provider);

        var exception = await Assert.ThrowsAsync<NarrativeInterpretationRequestException>(() => service.InterpretAsync(
            new("Player", "EUW", "euw1", "europe", 50, ""),
            "EUW1_1",
            new(start, end, reconstructionVersion, detectorVersion),
            CancellationToken.None));

        Assert.Equal(expected, exception.Kind);
        Assert.Equal(0, provider.CallCount);
    }

    private sealed class FakeSource(
        ReconstructionFailure? failure = null,
        bool withReviewWindow = false,
        bool withCounterRegressionAndIssue = false,
        bool withTemporalContractWindow = false) : IStoredReconstructionSource
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
            var hasEnemyJungler = withReviewWindow || withTemporalContractWindow;
            var participants = hasEnemyJungler
                ? new[] { participant, new Participant(7, 200, 200, "Belveth", "JUNGLE", true, 0, 0, 0) }
                : [participant];
            var frames = withTemporalContractWindow
                ? new[]
                {
                    new FrameObservation(31, 1_860_547, Observation(2, 20_608), Observation(7, 13_682)),
                    new FrameObservation(32, 1_920_579, Observation(2, 20_608), Observation(7, 13_682)),
                    new FrameObservation(33, 1_980_584, Observation(2, 21_000), Observation(7, 14_500)),
                    new FrameObservation(34, 2_040_596, Observation(2, 22_000), Observation(7, 15_800)),
                    new FrameObservation(35, 2_100_622, Observation(2, 22_842), Observation(7, 17_096))
                }
                : withReviewWindow
                    ? new[]
                {
                    new FrameObservation(0, 0, Observation(2, 1000, withCounterRegressionAndIssue ? 10 : 0), Observation(7, 1000)),
                    new FrameObservation(1, 90_000, Observation(2, 1000, withCounterRegressionAndIssue ? 10 : 0), Observation(7, 1000)),
                    new FrameObservation(2, 180_000, Observation(2, 2000, withCounterRegressionAndIssue ? 4 : 0), Observation(7, 1000))
                }
                    :
                    [
                        new FrameObservation(0, 0, Observation(2, 1000), null),
                        new FrameObservation(1, 100, Observation(2, 1100), null)
                    ];
            var enemyResolution = hasEnemyJungler
                ? new EnemyJunglerResolution(EnemyResolutionStatus.Resolved, 7)
                : new EnemyJunglerResolution(EnemyResolutionStatus.Missing, null);
            var issues = withCounterRegressionAndIssue
                ? new[] { new SourceDataIssue("test_source_gap", "timeline.frames[1]", "A synthetic source gap was detected.", "The available values were retained.") }
                : Array.Empty<SourceDataIssue>();
            var events = withTemporalContractWindow
                ? new ReconstructionEvent[]
                {
                    new ChampionKillEvent(1_970_458, new(33, 21), 7, 2, [], null, null, null)
                }
                : Array.Empty<ReconstructionEvent>();
            var input = new ReconstructionInput(matchId, "1.0", 420, 11, 2_101, participants, 2, enemyResolution, frames, events, issues);
            var metadata = new ReconstructionSourceMetadata(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "v5", "v5");
            return Task.FromResult(StoredReconstructionLoadResult.Success(new(input, metadata)));
        }

        private static PlayerObservation Observation(int participantId, long gold, long jungleCs = 0) =>
            new(participantId, null, gold, 0, 0, 1, jungleCs, 0, null, null, null, null);
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

    private sealed class FakeNarrativeProvider : INarrativeInterpretationProvider
    {
        public int CallCount { get; private set; }
        public List<NarrativeInterpretationInputV1> Inputs { get; } = [];

        public Task<NarrativeProviderResultV1> InterpretAsync(NarrativeInterpretationInputV1 input, CancellationToken cancellationToken)
        {
            CallCount++;
            Inputs.Add(input);
            var output = new NarrativeInterpretationModelOutputV1(
                1,
                input.InputFingerprint,
                new("Relative gold changed across the selected period.", "factSummary", ["observation:relativeGoldMovement"]),
                [],
                [],
                [new("The evidence does not establish why the relative gold changed.", "notCaptured", ["observation:relativeGoldMovement"])]);
            return Task.FromResult(new NarrativeProviderResultV1(output, "fake", "fake-model", DateTimeOffset.UnixEpoch));
        }
    }
}
