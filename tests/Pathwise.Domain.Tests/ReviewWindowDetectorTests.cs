using Pathwise.Domain.Reconstruction;
using Pathwise.Domain.ReviewWindows;

namespace Pathwise.Domain.Tests;

public sealed class ReviewWindowDetectorTests
{
    [Fact]
    public void ExactThresholdsTriggerSymmetricallyAndOppositeDirectionsRemainSeparateSignals()
    {
        var reconstruction = Create(
            [
                FrameAbsolute(0, 0, 1_000, 1_000, 1_000, 1_000),
                FrameAbsolute(1, 90_000, 1_000, 1_000, 1_000, 1_000),
                FrameAbsolute(2, 180_000, 2_000, 1_000, 1_000, 2_500)
            ],
            []);

        var result = Detect(reconstruction);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(ReviewSelectionReason.GoldAndXpChange, candidate.PrimarySelectionReason);
        Assert.Collection(candidate.Signals.OrderBy(x => x.Kind),
            gold =>
            {
                Assert.Equal(ReviewSignalKind.GoldDifferenceChange, gold.Kind);
                Assert.Equal(1_000, gold.SignedChange);
                Assert.Equal(ReviewSignalDirection.Increase, gold.Direction);
            },
            xp =>
            {
                Assert.Equal(ReviewSignalKind.XpDifferenceChange, xp.Kind);
                Assert.Equal(-1_500, xp.SignedChange);
                Assert.Equal(ReviewSignalDirection.Decrease, xp.Direction);
            });
    }

    [Fact]
    public void NegativeGoldBoundaryTriggersWhileValuesImmediatelyBelowThresholdDoNot()
    {
        var negative = Create([
            FrameAbsolute(0, 0, 1_000, 1_000, 1_000, 1_000),
            FrameAbsolute(1, 90_000, 1_000, 1_000, 1_000, 1_000),
            FrameAbsolute(2, 180_000, 1_000, 2_000, 1_000, 1_000)], []);
        var negativeSignal = Assert.Single(Assert.Single(Detect(negative).Candidates).Signals);
        Assert.Equal(-1_000, negativeSignal.SignedChange);
        Assert.Equal(ReviewSignalDirection.Decrease, negativeSignal.Direction);

        var below = Create([
            Frame(0, 0, 0, 0),
            Frame(1, 90_000, 0, 0),
            Frame(2, 180_000, 999, 1_499)], []);
        Assert.Empty(Detect(below).Candidates);
    }

    [Fact]
    public void UsesActualIrregularFramesAndSkipsInsufficientLookback()
    {
        var reconstruction = Create(
            [Frame(0, 34, 0, 0), Frame(1, 90_034, 0, 0), Frame(2, 180_034, 1_000, 1_500)],
            []);

        var result = Detect(reconstruction);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal((34L, 180_034L), (candidate.RequestedStartTimestampMs, candidate.RequestedEndTimestampMs));
        Assert.All(candidate.Signals, signal => Assert.Equal([34L, 180_034L], signal.SourceFrames.Select(x => x.TimestampMs)));
    }

    [Theory]
    [InlineData(EnemyResolutionStatus.Missing)]
    [InlineData(EnemyResolutionStatus.Ambiguous)]
    public void UnresolvedEnemyDisablesMetricsButKeepsDeathsAndObjectives(EnemyResolutionStatus status)
    {
        var events = new ReconstructionEvent[]
        {
            Kill(30_000, 0, victim: 2),
            new EliteMonsterKillEvent(90_000, new(1, 0), "DRAGON", null, null,
                new(TeamAttributionKind.Unknown, 0, null, "unknown"), [], null, null)
        };
        var reconstruction = Create(
            [Frame(0, 0, 0, 0, includeEnemy: false), Frame(1, 180_000, 5_000, 5_000, includeEnemy: false)],
            events,
            status);

        var result = Detect(reconstruction);

        Assert.Null(Assert.Single(result.Candidates).RelativeEvidence);
        Assert.Contains(result.Candidates.SelectMany(x => x.Signals), x => x.Kind == ReviewSignalKind.ConfiguredPlayerDeath);
        Assert.Contains(result.Candidates.SelectMany(x => x.Signals), x => x.Kind == ReviewSignalKind.EliteMonsterKill);
        Assert.Empty(result.SkippedComparisons);
    }

    [Fact]
    public void LargeGapsAndCounterRegressionsAreReportedPerMetric()
    {
        var options = ReviewWindowOptions.Default with { MetricLookbackMs = 100, MaximumAdjacentFrameGapMs = 90 };
        var gap = Create([Frame(0, 0, 0, 0), Frame(1, 100, 2_000, 2_000)], []);
        var gapResult = new ReviewWindowDetector().Detect(gap, options);
        Assert.Empty(gapResult.Candidates);
        Assert.Equal(2, gapResult.SkippedComparisons.Count(x => x.Reason == SkippedComparisonReason.AdjacentFrameGap));

        var regression = Create([
            FrameAbsolute(0, 0, 1_000, 1_000, 1_000, 1_000),
            FrameAbsolute(1, 50, 900, 1_100, 1_100, 1_100),
            FrameAbsolute(2, 100, 3_000, 2_000, 3_000, 2_000)], []);
        var regressionResult = new ReviewWindowDetector().Detect(regression, options with { MaximumAdjacentFrameGapMs = 100 });
        Assert.Contains(regressionResult.SkippedComparisons, x =>
            x.Metric == ReviewSignalKind.GoldDifferenceChange &&
            x.Reason == SkippedComparisonReason.ConfiguredPlayerCounterRegression &&
            x.PreviousValue == 1_000 && x.CurrentValue == 900);
        Assert.DoesNotContain(regressionResult.Candidates.SelectMany(x => x.Signals), x => x.Kind == ReviewSignalKind.GoldDifferenceChange);
    }

    [Fact]
    public void CombatLookbackIsClosedAndCountsDistinctSourceEventsNotDuplicateAssists()
    {
        var events = new ReconstructionEvent[]
        {
            Kill(10_000, 0, assists: [2, 2]),
            Kill(70_000, 1, killer: 2),
            Kill(70_000, 2, victim: 2)
        };
        var result = Detect(Create([Frame(0, 0, 0, 0), Frame(1, 130_000, 0, 0)], events));

        var cluster = Assert.Single(result.Candidates.SelectMany(x => x.Signals), x =>
            x.Kind == ReviewSignalKind.ConcentratedPlayerCombat && x.DistinctEventCount == 3);
        Assert.Equal(3, cluster.SourceEvents.Count);
        Assert.Equal((10_000L, 70_000L), (cluster.StartTimestampMs, cluster.EndTimestampMs));
    }

    [Fact]
    public void ObjectiveAndNullableShutdownFieldsRemainExactSupportingEvidence()
    {
        var kill = Kill(50_000, 0, killer: 2, shutdownBounty: null);
        var objective = new EliteMonsterKillEvent(60_000, new(1, 1), "DRAGON", null, null,
            new(TeamAttributionKind.Unknown, 0, null, "unresolved"), [], null, null);
        var candidate = Assert.Single(Detect(Create([Frame(0, 0, 0, 0), Frame(1, 120_000, 0, 0)], [kill, objective])).Candidates);

        Assert.Contains(candidate.SupportingEvents, x => ReferenceEquals(x, objective));
        Assert.Null(Assert.IsType<ChampionKillEvent>(candidate.SupportingEvents.Single(x => x.Source == kill.Source)).ShutdownBounty);
        Assert.Equal(TeamAttributionKind.Unknown, Assert.IsType<EliteMonsterKillEvent>(candidate.SupportingEvents.Single(x => x.Source == objective.Source)).TeamAttribution.Kind);
    }

    [Fact]
    public void DuplicateBoundaryAndCapAreDeterministicWithoutForcingAQuota()
    {
        var events = Enumerable.Range(0, 7)
            .Select(index => (ReconstructionEvent)new EliteMonsterKillEvent(
                100_000 + (index * 200_000), new(index + 1, 0), "DRAGON", null, null,
                new(TeamAttributionKind.Neutral, 300, null, null), [], null, null))
            .ToArray();
        var frames = Enumerable.Range(0, 24).Select(index => Frame(index, index * 60_000L, 0, 0)).ToArray();
        var result = Detect(Create(frames, events));

        Assert.Equal(5, result.Candidates.Count);
        Assert.Equal(2, result.UnselectedDueToCap.Count);
        Assert.Equal(Enumerable.Range(1, 5), result.Candidates.Select(x => x.SelectionRank));

        var sparse = Detect(Create([Frame(0, 0, 0, 0), Frame(1, 180_000, 0, 0)], []));
        Assert.Empty(sparse.Candidates);
    }

    [Fact]
    public void RepeatedOverlapChainsMergeWithoutRepadding()
    {
        var events = new ReconstructionEvent[]
        {
            Objective(100_000, 0),
            Objective(200_000, 1),
            Objective(300_000, 2)
        };
        var reconstruction = Create(
            Enumerable.Range(0, 7).Select(index => Frame(index, index * 60_000L, 0, 0)).ToArray(),
            events);
        var options = ReviewWindowOptions.Default with { MaximumMergedDurationMs = 400_000 };

        var candidate = Assert.Single(new ReviewWindowDetector().Detect(reconstruction, options).Candidates);

        Assert.Equal((40_000L, 360_000L), (candidate.RequestedStartTimestampMs, candidate.RequestedEndTimestampMs));
        Assert.Equal(3, candidate.Signals.Count);
        Assert.Equal(2, candidate.AbsorbedSignals.Count);
    }

    [Fact]
    public void DuplicateOverlapAtExactlyHalfIsSuppressedAndReported()
    {
        var reconstruction = Create(
            [Frame(0, 0, 0, 0), Frame(1, 400_000, 0, 0)],
            [Kill(100_000, 0, victim: 2), Kill(200_000, 1, victim: 2)]);
        var options = ReviewWindowOptions.Default with { EventContextPaddingMs = 100_000, MaximumMergedDurationMs = 50_000 };

        var result = new ReviewWindowDetector().Detect(reconstruction, options);

        var candidate = Assert.Single(result.Candidates);
        var suppressed = Assert.Single(result.SuppressedSeeds);
        Assert.Equal(SuppressionReason.DuplicateOverlap, suppressed.Reason);
        Assert.Equal(candidate.SelectionRank, suppressed.SelectedWindowRank);
        Assert.Single(candidate.SuppressedSeeds);
    }

    [Fact]
    public void CosmeticParticipantAndMatchFactsDoNotAffectDetection()
    {
        var frames = new[] { Frame(0, 0, 0, 0), Frame(1, 180_000, 1_000, 0) };
        var original = Create(frames, []);
        var changed = Create(frames, [], patch: "different", configuredName: "Different", configuredWon: true);

        var originalResult = Detect(original);
        var changedResult = Detect(changed);

        Assert.Equal(originalResult.Candidates.Select(x => (x.RequestedStartTimestampMs, x.RequestedEndTimestampMs, x.PrimarySelectionReason)),
            changedResult.Candidates.Select(x => (x.RequestedStartTimestampMs, x.RequestedEndTimestampMs, x.PrimarySelectionReason)));
    }

    private static ReviewWindowDetectionResult Detect(GameReconstruction reconstruction) =>
        new ReviewWindowDetector().Detect(reconstruction, ReviewWindowOptions.Default);

    private static GameReconstruction Create(
        IReadOnlyList<FrameObservation> frames,
        IReadOnlyList<ReconstructionEvent> events,
        EnemyResolutionStatus enemyStatus = EnemyResolutionStatus.Resolved,
        string patch = "1.0",
        string configuredName = "Khazix",
        bool configuredWon = false)
    {
        var participants = new[]
        {
            new Participant(2, 100, 121, configuredName, "JUNGLE", configuredWon, 0, 0, 0),
            new Participant(7, 200, 200, "Belveth", "JUNGLE", !configuredWon, 0, 0, 0)
        };
        return new(new("EUW1_1", patch, 420, 11, (int)(frames[^1].TimestampMs / 1_000), participants, 2,
            new(enemyStatus, enemyStatus == EnemyResolutionStatus.Resolved ? 7 : null), frames, events, []));
    }

    private static FrameObservation Frame(int index, long timestamp, long relativeGold, long relativeXp, bool includeEnemy = true) =>
        FrameAbsolute(index, timestamp, 1_000 + relativeGold, 1_000, 1_000 + relativeXp, 1_000, includeEnemy);

    private static FrameObservation FrameAbsolute(
        int index, long timestamp, long playerGold, long enemyGold, long playerXp, long enemyXp, bool includeEnemy = true) =>
        new(index, timestamp,
            Observation(2, playerGold, playerXp),
            includeEnemy ? Observation(7, enemyGold, enemyXp) : null);

    private static PlayerObservation Observation(int id, long gold, long xp) =>
        new(id, null, gold, 0, xp, 1, 0, 0, null, null, null, null);

    private static ChampionKillEvent Kill(
        long timestamp,
        int eventIndex,
        int? killer = null,
        int victim = 7,
        IReadOnlyList<int>? assists = null,
        int? shutdownBounty = 0) =>
        new(timestamp, new(1, eventIndex), killer, victim, assists ?? [], null, 300, shutdownBounty);

    private static EliteMonsterKillEvent Objective(long timestamp, int eventIndex) =>
        new(timestamp, new(1, eventIndex), "DRAGON", null, null,
            new(TeamAttributionKind.Neutral, 300, null, null), [], null, null);
}
