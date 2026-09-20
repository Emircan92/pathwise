using Pathwise.Domain.FactualObservations;
using Pathwise.Domain.Reconstruction;
using Pathwise.Domain.ReviewWindows;

namespace Pathwise.Domain.Tests;

public sealed class FactualObservationGeneratorTests
{
    [Theory]
    [InlineData(FactualObservationKind.RelativeGoldMovement, 499, false)]
    [InlineData(FactualObservationKind.RelativeGoldMovement, 500, true)]
    [InlineData(FactualObservationKind.RelativeGoldMovement, 501, true)]
    [InlineData(FactualObservationKind.RelativeGoldMovement, -499, false)]
    [InlineData(FactualObservationKind.RelativeGoldMovement, -500, true)]
    [InlineData(FactualObservationKind.RelativeGoldMovement, -501, true)]
    [InlineData(FactualObservationKind.RelativeXpMovement, 749, false)]
    [InlineData(FactualObservationKind.RelativeXpMovement, 750, true)]
    [InlineData(FactualObservationKind.RelativeXpMovement, 751, true)]
    [InlineData(FactualObservationKind.RelativeXpMovement, -749, false)]
    [InlineData(FactualObservationKind.RelativeXpMovement, -750, true)]
    [InlineData(FactualObservationKind.RelativeXpMovement, -751, true)]
    [InlineData(FactualObservationKind.RelativeJungleCsMovement, 9, false)]
    [InlineData(FactualObservationKind.RelativeJungleCsMovement, 10, true)]
    [InlineData(FactualObservationKind.RelativeJungleCsMovement, 11, true)]
    [InlineData(FactualObservationKind.RelativeJungleCsMovement, -9, false)]
    [InlineData(FactualObservationKind.RelativeJungleCsMovement, -10, true)]
    [InlineData(FactualObservationKind.RelativeJungleCsMovement, -11, true)]
    public void MetricThresholdsAreInclusiveAndSymmetric(
        FactualObservationKind kind,
        long signedChange,
        bool expected)
    {
        var start = Metrics(10_000, 10_000, 100);
        var configuredEnd = start;
        var enemyEnd = start;
        if (signedChange >= 0)
            configuredEnd = Add(start, kind, signedChange);
        else
            enemyEnd = Add(start, kind, -signedChange);
        var reconstruction = Create([
            Frame(0, 0, start, start),
            Frame(1, 100, configuredEnd, enemyEnd)
        ]);

        var window = Assert.Single(Generate(reconstruction, (0, 100)).Windows);

        Assert.Equal(expected, window.Observations.Any(observation => observation.Key.Kind == kind));
    }

    [Fact]
    public void ZeroUnchangedRelativeValuesAndSmallSignCrossingsAreOmitted()
    {
        var unchanged = Create([
            Frame(0, 0, Metrics(1_000, 1_000, 10), Metrics(1_000, 1_000, 10)),
            Frame(1, 100, Metrics(2_000, 2_000, 20), Metrics(2_000, 2_000, 20))
        ]);
        Assert.Empty(Assert.Single(Generate(unchanged, (0, 100)).Windows).Observations);

        var crossing = Create([
            Frame(0, 0, Metrics(1_100, 1_100, 11), Metrics(1_000, 1_000, 10)),
            Frame(1, 100, Metrics(1_100, 1_100, 11), Metrics(1_200, 1_200, 12))
        ]);
        Assert.Empty(Assert.Single(Generate(crossing, (0, 100)).Windows).Observations);
    }

    [Theory]
    [InlineData(EnemyResolutionStatus.Missing)]
    [InlineData(EnemyResolutionStatus.Ambiguous)]
    public void UnresolvedEnemySuppressesRelativeMetricsButRetainsEvents(EnemyResolutionStatus status)
    {
        var events = new ReconstructionEvent[]
        {
            Kill(50, 0, victim: 2),
            Objective(60, 1)
        };
        var reconstruction = Create([
            Frame(0, 0, Metrics(0, 0, 0), null),
            Frame(1, 100, Metrics(5_000, 5_000, 50), null)
        ], events, status);

        var observations = Assert.Single(Generate(reconstruction, (0, 100)).Windows).Observations;

        Assert.Collection(observations,
            observation => Assert.IsType<ConfiguredPlayerCombatObservation>(observation),
            observation => Assert.IsType<EliteObjectiveContextObservation>(observation));
    }

    [Fact]
    public void CombatSummaryDeduplicatesSourcesAndHonorsOpenClosedBoundary()
    {
        var events = new ReconstructionEvent[]
        {
            Kill(10, 0, killer: 2),
            Kill(20, 1, killer: null, victim: 2),
            Kill(30, 2, assists: [2, 2]),
            Kill(30, 3, killer: 2, assists: [2]),
            Kill(30, 3, killer: 2, assists: [2]),
            Kill(40, 4, victim: 2)
        };
        var reconstruction = Create([
            Frame(0, 0, Metrics(0, 0, 0), Metrics(0, 0, 0)),
            Frame(1, 40, Metrics(0, 0, 0), Metrics(0, 0, 0))
        ], events);

        var combat = Assert.IsType<ConfiguredPlayerCombatObservation>(
            Assert.Single(Assert.Single(Generate(reconstruction, (10, 40)).Windows).Observations));

        Assert.Equal((1, 2, 2, 4),
            (combat.KillCount, combat.DeathCount, combat.AssistCount, combat.DistinctEventCount));
        Assert.Equal([(20L, 1), (30L, 2), (30L, 3), (40L, 4)],
            combat.Events.Select(@event => (@event.TimestampMs, @event.Source.EventIndex)));
        Assert.Null(combat.Events[0].KillerParticipantId);
    }

    [Fact]
    public void ObjectiveSummaryPreservesAllDistinctEventsAndAttribution()
    {
        var atStart = Objective(10, 0);
        var dragon = Objective(20, 1, "DRAGON", "HEXTECH_DRAGON", TeamAttributionKind.KnownTeam);
        var baron = Objective(20, 2, "BARON_NASHOR", null, TeamAttributionKind.Unknown);
        var reconstruction = Create([
            Frame(0, 0, Metrics(0, 0, 0), Metrics(0, 0, 0)),
            Frame(1, 20, Metrics(0, 0, 0), Metrics(0, 0, 0))
        ], [atStart, dragon, baron]);

        var observation = Assert.IsType<EliteObjectiveContextObservation>(
            Assert.Single(Assert.Single(Generate(reconstruction, (10, 20)).Windows).Observations));

        Assert.Equal([dragon.Source, baron.Source], observation.Events.Select(@event => @event.Source));
        Assert.Equal(TeamAttributionKind.KnownTeam, observation.Events[0].TeamAttribution.Kind);
        Assert.Equal(TeamAttributionKind.Unknown, observation.Events[1].TeamAttribution.Kind);
    }

    [Fact]
    public void CounterRegressionOmitsOnlyAffectedMetricAndConfiguredPlayerWinsSameFrameTie()
    {
        var reconstruction = Create([
            Frame(0, 0, Metrics(1_000, 1_000, 10), Metrics(1_000, 1_000, 10)),
            Frame(1, 200_000, Metrics(900, 2_000, 9), Metrics(900, 1_000, 9)),
            Frame(2, 400_000, Metrics(2_000, 3_000, 30), Metrics(1_000, 1_000, 10))
        ]);

        var window = Assert.Single(Generate(reconstruction, (0, 400_000)).Windows);

        Assert.Equal([
            FactualObservationKind.RelativeXpMovement
        ], window.Observations.Select(observation => observation.Key.Kind));
        Assert.Equal([
            FactualObservationKind.RelativeGoldMovement,
            FactualObservationKind.RelativeJungleCsMovement
        ], window.Omissions.Select(omission => omission.Kind));
        Assert.All(window.Omissions, omission => Assert.Equal(2, omission.Regression.ParticipantId));
        Assert.Equal((0, 1),
            (window.Omissions[0].Regression.PreviousFrame.FrameIndex, window.Omissions[0].Regression.CurrentFrame.FrameIndex));
    }

    [Fact]
    public void CounterRegressionUsesEarliestTimestampBeforeParticipantTieBreak()
    {
        var reconstruction = Create([
            Frame(0, 0, Metrics(1_000, 0, 0), Metrics(1_000, 0, 0)),
            Frame(1, 10, Metrics(1_100, 0, 0), Metrics(900, 0, 0)),
            Frame(2, 20, Metrics(1_050, 0, 0), Metrics(1_000, 0, 0)),
            Frame(3, 30, Metrics(2_000, 0, 0), Metrics(1_000, 0, 0))
        ]);

        var omission = Assert.Single(Assert.Single(Generate(reconstruction, (0, 30)).Windows).Omissions);

        Assert.Equal(FactualObservationKind.RelativeGoldMovement, omission.Kind);
        Assert.Equal(7, omission.Regression.ParticipantId);
        Assert.Equal((0, 1),
            (omission.Regression.PreviousFrame.FrameIndex, omission.Regression.CurrentFrame.FrameIndex));
    }

    [Fact]
    public void SparseFramesDoNotSuppressEndpointObservations()
    {
        var reconstruction = Create([
            Frame(0, 0, Metrics(1_000, 1_000, 10), Metrics(1_000, 1_000, 10)),
            Frame(1, 1_000_000, Metrics(1_500, 1_750, 20), Metrics(1_000, 1_000, 10))
        ]);

        var observations = Assert.Single(Generate(reconstruction, (0, 1_000_000)).Windows).Observations;

        Assert.Equal(3, observations.Count);
    }

    [Fact]
    public void SharedFloorFrameRetainsIntervalEventsWithoutInventingMetricMovement()
    {
        var reconstruction = Create([
            Frame(0, 0, Metrics(1_000, 1_000, 10), Metrics(1_000, 1_000, 10)),
            Frame(1, 100, Metrics(2_000, 2_000, 20), Metrics(1_000, 1_000, 10))
        ], [Kill(20, 0, victim: 2)]);

        var window = Assert.Single(Generate(reconstruction, (10, 20)).Windows);

        var combat = Assert.IsType<ConfiguredPlayerCombatObservation>(Assert.Single(window.Observations));
        Assert.Single(combat.Events);
        Assert.Empty(window.Omissions);
    }

    [Fact]
    public void OrderingKeysPolicyAndFinalWindowEndpointsAreDeterministic()
    {
        var events = new ReconstructionEvent[] { Kill(75, 0, victim: 2), Objective(80, 1) };
        var reconstruction = Create([
            Frame(0, 0, Metrics(1_000, 1_000, 10), Metrics(1_000, 1_000, 10)),
            Frame(1, 50, Metrics(1_100, 1_100, 11), Metrics(1_000, 1_000, 10)),
            Frame(2, 100, Metrics(1_600, 1_800, 21), Metrics(1_000, 1_000, 10))
        ], events, patch: "cosmetic-patch", configuredName: "CosmeticName", configuredWon: true);

        var first = Generate(reconstruction, (25, 100));
        var second = Generate(reconstruction, (25, 100));
        var window = Assert.Single(first.Windows);

        Assert.Equal(1, first.GeneratorVersion);
        Assert.Equal(new(500, 750, 10), first.EffectivePolicy);
        Assert.Equal(Enum.GetValues<FactualObservationKind>(), window.Observations.Select(x => x.Key.Kind));
        Assert.Equal(window.Observations.Select(x => x.Key), Assert.Single(second.Windows).Observations.Select(x => x.Key));
        Assert.All(window.Observations, observation =>
        {
            Assert.Equal("EUW1_1", observation.Key.Window.MatchId);
            Assert.Equal(25, observation.Key.Window.RequestedStartTimestampMs);
            Assert.Equal(100, observation.Key.Window.RequestedEndTimestampMs);
            Assert.Equal(1, observation.Key.GeneratorVersion);
        });
        var gold = Assert.IsType<RelativeGoldObservation>(window.Observations[0]);
        Assert.Equal((0, 0L), (gold.Evidence.StartFrame.FrameIndex, gold.Evidence.StartFrame.TimestampMs));
        Assert.Equal((2, 100L), (gold.Evidence.EndFrame.FrameIndex, gold.Evidence.EndFrame.TimestampMs));
        Assert.Equal(600, gold.Evidence.SignedChange);
    }

    [Fact]
    public void OverlappingWindowsRetainSharedEventsWithDifferentWindowKeys()
    {
        var shared = Kill(75, 0, victim: 2);
        var reconstruction = Create([
            Frame(0, 0, Metrics(0, 0, 0), Metrics(0, 0, 0)),
            Frame(1, 100, Metrics(0, 0, 0), Metrics(0, 0, 0))
        ], [shared]);

        var result = Generate(reconstruction, (0, 100), (50, 100));

        Assert.Equal(2, result.Windows.Count);
        Assert.All(result.Windows, window => Assert.Single(
            Assert.IsType<ConfiguredPlayerCombatObservation>(Assert.Single(window.Observations)).Events));
        Assert.NotEqual(result.Windows[0].Observations[0].Key, result.Windows[1].Observations[0].Key);
    }

    [Fact]
    public void CosmeticMatchFactsDoNotChangeObservationCalculations()
    {
        var frames = new[]
        {
            Frame(0, 0, Metrics(1_000, 1_000, 10), Metrics(1_000, 1_000, 10)),
            Frame(1, 100, Metrics(1_500, 1_750, 20), Metrics(1_000, 1_000, 10))
        };
        var original = Generate(Create(frames), (0, 100));
        var cosmetic = Generate(Create(frames, patch: "different", configuredName: "Different", configuredWon: true), (0, 100));

        Assert.Equal(
            Assert.Single(original.Windows).Observations.Select(ObservationValues),
            Assert.Single(cosmetic.Windows).Observations.Select(ObservationValues));
    }

    [Fact]
    public void InconsistentEndpointEvidenceIsRejected()
    {
        var reconstruction = Create([
            Frame(0, 0, Metrics(0, 0, 0), Metrics(0, 0, 0)),
            Frame(1, 100, Metrics(500, 0, 0), Metrics(0, 0, 0))
        ]);
        var detection = Detection(reconstruction, (0, 100));
        var candidate = detection.Candidates[0];
        var invalidState = candidate.Changes.StartState with { SelectedFrameIndex = 99 };
        var invalidChanges = candidate.Changes with { StartState = invalidState };
        var invalid = detection with { Candidates = [candidate with { Changes = invalidChanges }] };

        Assert.Throws<ArgumentException>(() => new FactualObservationGenerator().Generate(reconstruction, invalid));
    }

    private static FactualObservationResult Generate(
        GameReconstruction reconstruction,
        params (long Start, long End)[] windows) =>
        new FactualObservationGenerator().Generate(reconstruction, Detection(reconstruction, windows));

    private static (FactualObservationKind Kind, long Change) ObservationValues(FactualObservation observation) => observation switch
    {
        RelativeGoldObservation gold => (gold.Key.Kind, gold.Evidence.SignedChange),
        RelativeXpObservation xp => (xp.Key.Kind, xp.Evidence.SignedChange),
        RelativeJungleCsObservation jungleCs => (jungleCs.Key.Kind, jungleCs.Evidence.SignedChange),
        _ => (observation.Key.Kind, 0)
    };

    private static ReviewWindowDetectionResult Detection(
        GameReconstruction reconstruction,
        params (long Start, long End)[] windows)
    {
        var configured = reconstruction.Participants.Single(participant => participant.ParticipantId == 2);
        var enemy = reconstruction.EnemyResolution.ParticipantId is { } enemyId
            ? reconstruction.Participants.Single(participant => participant.ParticipantId == enemyId)
            : null;
        var candidates = windows.Select((window, index) =>
        {
            var changes = reconstruction.Changes(window.Start, window.End);
            return new ReviewWindowCandidate(
                index + 1,
                ReviewSelectionReason.ConfiguredPlayerDeath,
                window.Start,
                window.End,
                configured,
                enemy,
                reconstruction.EnemyResolution,
                changes,
                null,
                [],
                [],
                [],
                changes.Events,
                reconstruction.SourceDataIssues,
                []);
        }).ToArray();
        return new(
            candidates,
            ReviewWindowDetector.CurrentVersion,
            reconstruction.ReconstructionVersion,
            ReviewWindowOptions.Default,
            reconstruction.EnemyResolution,
            reconstruction.SourceDataIssues,
            [],
            [],
            []);
    }

    private static GameReconstruction Create(
        IReadOnlyList<FrameObservation> frames,
        IReadOnlyList<ReconstructionEvent>? events = null,
        EnemyResolutionStatus status = EnemyResolutionStatus.Resolved,
        string patch = "1.0",
        string configuredName = "Khazix",
        bool configuredWon = false)
    {
        var participants = new[]
        {
            new Participant(2, 100, 121, configuredName, "JUNGLE", configuredWon, 0, 0, 0),
            new Participant(7, 200, 200, "Belveth", "JUNGLE", !configuredWon, 0, 0, 0)
        };
        return new(new(
            "EUW1_1",
            patch,
            420,
            11,
            (int)(frames[^1].TimestampMs / 1_000),
            participants,
            2,
            new(status, status == EnemyResolutionStatus.Resolved ? 7 : null),
            frames,
            events ?? [],
            []));
    }

    private static FrameObservation Frame(int index, long timestamp, MetricValues configured, MetricValues? enemy) =>
        new(index, timestamp, Observation(2, configured), enemy is null ? null : Observation(7, enemy));

    private static PlayerObservation Observation(int participantId, MetricValues values) =>
        new(participantId, null, values.Gold, 0, values.Xp, 1, values.JungleCs, 0, null, null, null, null);

    private static MetricValues Metrics(long gold, long xp, long jungleCs) => new(gold, xp, jungleCs);

    private static MetricValues Add(MetricValues values, FactualObservationKind kind, long amount) => kind switch
    {
        FactualObservationKind.RelativeGoldMovement => values with { Gold = values.Gold + amount },
        FactualObservationKind.RelativeXpMovement => values with { Xp = values.Xp + amount },
        FactualObservationKind.RelativeJungleCsMovement => values with { JungleCs = values.JungleCs + amount },
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static ChampionKillEvent Kill(
        long timestamp,
        int eventIndex,
        int? killer = null,
        int victim = 7,
        IReadOnlyList<int>? assists = null) =>
        new(timestamp, new(1, eventIndex), killer, victim, assists ?? [], null, 300, null);

    private static EliteMonsterKillEvent Objective(
        long timestamp,
        int eventIndex,
        string? monsterType = "DRAGON",
        string? monsterSubType = null,
        TeamAttributionKind attribution = TeamAttributionKind.Neutral) =>
        new(
            timestamp,
            new(1, eventIndex),
            monsterType,
            monsterSubType,
            attribution == TeamAttributionKind.KnownTeam ? 7 : null,
            new(attribution, attribution == TeamAttributionKind.Neutral ? 300 : 200,
                attribution == TeamAttributionKind.KnownTeam ? 200 : null, null),
            [],
            null,
            null);

    private sealed record MetricValues(long Gold, long Xp, long JungleCs);
}
