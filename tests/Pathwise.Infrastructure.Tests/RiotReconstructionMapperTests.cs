using System.Text.Json;
using System.Text.Json.Nodes;
using Pathwise.Domain.FactualObservations;
using Pathwise.Domain.Reconstruction;
using Pathwise.Domain.ReviewWindows;
using Pathwise.Infrastructure.Reconstruction;

namespace Pathwise.Infrastructure.Tests;

public sealed class RiotReconstructionMapperTests
{
    private const string MatchId = "EUW1_1";
    private const string ConfiguredPuuid = "participant-2-puuid";
    private readonly RiotReconstructionMapper _mapper = new();

    [Fact]
    public void FixtureProducesApprovedReviewWindowsWithTraceableEvidence()
    {
        var reconstruction = MapFixture();
        var result = new ReviewWindowDetector().Detect(reconstruction, ReviewWindowOptions.Default);

        Assert.Equal(ReviewWindowDetector.CurrentVersion, result.DetectorVersion);
        Assert.Equal(5, result.Candidates.Count);
        Assert.Empty(result.SkippedComparisons);
        Assert.Equal(new[]
        {
            (180_034L, 360_067L),
            (900_315L, 1_085_356L),
            (1_200_393L, 1_380_440L),
            (1_421_654L, 1_620_500L),
            (1_617_047L, 1_691_676L)
        }, result.Candidates.Select(x => (x.RequestedStartTimestampMs, x.RequestedEndTimestampMs)));

        var first = result.Candidates[0];
        var firstGold = Assert.Single(first.Signals, x =>
            x.Kind == ReviewSignalKind.GoldDifferenceChange && x.StartTimestampMs == 180_034 && x.EndTimestampMs == 360_067);
        Assert.Equal((28L, 1_193L, 1_165L), (firstGold.StartValue, firstGold.EndValue, firstGold.SignedChange));
        Assert.Equal(360_067, first.Changes.EndState.SelectedFrameTimestampMs);
        Assert.Empty(first.TriggerEventReferences);

        var fourth = result.Candidates[3];
        var combinedGold = Assert.Single(fourth.Signals, x =>
            x.Kind == ReviewSignalKind.GoldDifferenceChange && x.StartTimestampMs == 1_440_464 && x.EndTimestampMs == 1_620_500);
        var combinedXp = Assert.Single(fourth.Signals, x =>
            x.Kind == ReviewSignalKind.XpDifferenceChange && x.StartTimestampMs == 1_440_464 && x.EndTimestampMs == 1_620_500);
        Assert.Equal((4_753L, 3_182L, -1_571L), (combinedGold.StartValue, combinedGold.EndValue, combinedGold.SignedChange));
        Assert.Equal((-1_479L, -4_182L, -2_703L), (combinedXp.StartValue, combinedXp.EndValue, combinedXp.SignedChange));
        Assert.Equal((4_742L, 3_182L, -1_560L),
            (fourth.RelativeEvidence!.Start.TotalGold, fourth.RelativeEvidence.End.TotalGold, fourth.RelativeEvidence.Change.TotalGold));
        Assert.Equal((154L, 165L),
            (fourth.Changes.StartState.ConfiguredPlayer.Observation.JungleCs, fourth.Changes.EndState.ConfiguredPlayer.Observation.JungleCs));
        Assert.Contains(fourth.SupportingEvents.OfType<EliteMonsterKillEvent>(), x => x.MonsterType == "DRAGON");
        Assert.Contains(fourth.SupportingEvents.OfType<EliteMonsterKillEvent>(), x => x.MonsterType == "BARON_NASHOR");
        Assert.DoesNotContain(result.Candidates.SelectMany(x => x.Signals), x => x.Kind == ReviewSignalKind.EliteMonsterKill);

        var last = result.Candidates[4];
        Assert.Equal(1_560_471, last.Changes.StartState.SelectedFrameTimestampMs);
        Assert.Contains(last.SupportingEvents.OfType<ChampionKillEvent>(), x => x.TimestampMs == 1_644_174 && x.ShutdownBounty == 396);
        Assert.Contains(last.SupportingEvents.OfType<ChampionKillEvent>(), x => x.TimestampMs == 1_649_828 && x.ShutdownBounty == 37);
        Assert.Contains(result.SourceIssues, x => x.Code == "team_attribution_unknown");

        var factual = new FactualObservationGenerator().Generate(reconstruction, result);
        Assert.Equal(FactualObservationGenerator.CurrentVersion, factual.GeneratorVersion);
        Assert.Equal(new(500, 750, 10), factual.EffectivePolicy);
        var observedFourth = factual.Windows[3];
        Assert.Equal((1_421_654L, 1_620_500L),
            (observedFourth.Window.RequestedStartTimestampMs, observedFourth.Window.RequestedEndTimestampMs));
        Assert.Empty(observedFourth.Omissions);
        Assert.Collection(observedFourth.Observations,
            observation =>
            {
                var gold = Assert.IsType<RelativeGoldObservation>(observation);
                Assert.Equal((23, 1_380_440L), (gold.Evidence.StartFrame.FrameIndex, gold.Evidence.StartFrame.TimestampMs));
                Assert.Equal((27, 1_620_500L), (gold.Evidence.EndFrame.FrameIndex, gold.Evidence.EndFrame.TimestampMs));
                Assert.Equal((14_650L, 16_243L, 1_593L),
                    (gold.Evidence.ConfiguredPlayer.StartValue, gold.Evidence.ConfiguredPlayer.EndValue, gold.Evidence.ConfiguredPlayer.Change));
                Assert.Equal((9_908L, 13_061L, 3_153L),
                    (gold.Evidence.EnemyJungler.StartValue, gold.Evidence.EnemyJungler.EndValue, gold.Evidence.EnemyJungler.Change));
                Assert.Equal((4_742L, 3_182L, -1_560L),
                    (gold.Evidence.RelativeStart, gold.Evidence.RelativeEnd, gold.Evidence.SignedChange));
            },
            observation =>
            {
                var xp = Assert.IsType<RelativeXpObservation>(observation);
                Assert.Equal((12_401L, 13_501L, 1_100L),
                    (xp.Evidence.ConfiguredPlayer.StartValue, xp.Evidence.ConfiguredPlayer.EndValue, xp.Evidence.ConfiguredPlayer.Change));
                Assert.Equal((13_257L, 17_683L, 4_426L),
                    (xp.Evidence.EnemyJungler.StartValue, xp.Evidence.EnemyJungler.EndValue, xp.Evidence.EnemyJungler.Change));
                Assert.Equal((-856L, -4_182L, -3_326L),
                    (xp.Evidence.RelativeStart, xp.Evidence.RelativeEnd, xp.Evidence.SignedChange));
            },
            observation =>
            {
                var jungleCs = Assert.IsType<RelativeJungleCsObservation>(observation);
                Assert.Equal((154L, 165L, 11L),
                    (jungleCs.Evidence.ConfiguredPlayer.StartValue, jungleCs.Evidence.ConfiguredPlayer.EndValue, jungleCs.Evidence.ConfiguredPlayer.Change));
                Assert.Equal((158L, 190L, 32L),
                    (jungleCs.Evidence.EnemyJungler.StartValue, jungleCs.Evidence.EnemyJungler.EndValue, jungleCs.Evidence.EnemyJungler.Change));
                Assert.Equal((-4L, -25L, -21L),
                    (jungleCs.Evidence.RelativeStart, jungleCs.Evidence.RelativeEnd, jungleCs.Evidence.SignedChange));
            },
            observation =>
            {
                var combat = Assert.IsType<ConfiguredPlayerCombatObservation>(observation);
                Assert.Equal((0, 2, 1, 3),
                    (combat.KillCount, combat.DeathCount, combat.AssistCount, combat.DistinctEventCount));
                Assert.Equal(new[] { (25, 22), (27, 0), (27, 4) },
                    combat.Events.Select(@event => (@event.Source.FrameIndex, @event.Source.EventIndex)));
            },
            observation =>
            {
                var objectives = Assert.IsType<EliteObjectiveContextObservation>(observation);
                Assert.Equal(new[] { (24, 30), (26, 7) },
                    objectives.Events.Select(@event => (@event.Source.FrameIndex, @event.Source.EventIndex)));
                Assert.Equal(new[] { "DRAGON", "BARON_NASHOR" }, objectives.Events.Select(@event => @event.MonsterType));
                Assert.Equal(new[] { "HEXTECH_DRAGON", null }, objectives.Events.Select(@event => @event.MonsterSubType));
                Assert.All(objectives.Events, @event =>
                {
                    Assert.Equal(7, @event.KillerParticipantId);
                    Assert.Equal(200, @event.TeamAttribution.ResolvedTeamId);
                });
            });
        Assert.DoesNotContain(observedFourth.Observations, observation =>
            observation.Key.Kind.ToString().Contains("Level", StringComparison.Ordinal));
    }

    [Fact]
    public void FixtureMapsRosterFramesEventsAttributionAndFinalKda()
    {
        var reconstruction = MapFixture();

        Assert.Equal(10, reconstruction.Participants.Count);
        Assert.Equal(2, reconstruction.ConfiguredParticipantId);
        Assert.Equal(new EnemyJunglerResolution(EnemyResolutionStatus.Resolved, 7), reconstruction.EnemyResolution);
        Assert.Equal(30, reconstruction.Observations.Count);
        Assert.Equal(1691676, reconstruction.AvailableToMs);
        Assert.Equal((16, 6, 3), Kda(reconstruction.StateAt(1691676).ConfiguredPlayer));
        Assert.Equal((11, 4, 5), Kda(reconstruction.StateAt(1691676).EnemyJungler!));

        var unknownSoul = Assert.Single(reconstruction.Events.OfType<DragonSoulMarkerEvent>(), x => x.TimestampMs == 759248);
        Assert.Equal(TeamAttributionKind.Unknown, unknownSoul.TeamAttribution.Kind);
        Assert.Equal(0, unknownSoul.TeamAttribution.SuppliedTeamId);
        var knownSoul = Assert.Single(reconstruction.Events.OfType<DragonSoulMarkerEvent>(), x => x.TimestampMs == 1428216);
        Assert.Equal(200, knownSoul.TeamAttribution.ResolvedTeamId);
        Assert.Contains(reconstruction.Events.OfType<EliteMonsterKillEvent>(), x => x.TimestampMs == 1505113 && x.MonsterType == "BARON_NASHOR");
        Assert.Contains(reconstruction.Events.OfType<EliteMonsterKillEvent>(), x =>
            x.TeamAttribution.ResolvedTeamId is { } teamId &&
            x.AssistingParticipantIds.Any(assist => reconstruction.Participants.Single(p => p.ParticipantId == assist).TeamId != teamId));
    }

    [Theory]
    [InlineData(300065, 2577, 2210, 5, 36, 2, 0, 0, 1979, 1915, 5, 36, 0, 0, 1)]
    [InlineData(600219, 5375, 4689, 8, 74, 5, 1, 0, 3500, 4044, 7, 75, 0, 1, 1)]
    [InlineData(900315, 7523, 7554, 11, 114, 6, 1, 0, 5723, 7193, 10, 107, 1, 2, 1)]
    [InlineData(1200393, 11654, 10772, 13, 142, 11, 2, 1, 8533, 11230, 13, 138, 3, 3, 4)]
    [InlineData(1500470, 15740, 13069, 15, 162, 14, 4, 2, 11457, 15633, 16, 174, 6, 4, 4)]
    public void FixtureMatchesReviewedCheckpoints(
        long timestamp,
        long playerGold, long playerXp, int playerLevel, long playerCs, int playerKills, int playerDeaths, int playerAssists,
        long enemyGold, long enemyXp, int enemyLevel, long enemyCs, int enemyKills, int enemyDeaths, int enemyAssists)
    {
        var state = MapFixture().StateAt(timestamp);

        AssertPlayer(state.ConfiguredPlayer, playerGold, playerXp, playerLevel, playerCs, playerKills, playerDeaths, playerAssists);
        AssertPlayer(state.EnemyJungler!, enemyGold, enemyXp, enemyLevel, enemyCs, enemyKills, enemyDeaths, enemyAssists);
    }

    [Fact]
    public void FixtureUsesFloorFrameAndProducesReviewedChanges()
    {
        var reconstruction = MapFixture();

        Assert.Equal(840296, reconstruction.StateAt(900000).SelectedFrameTimestampMs);
        var changes = reconstruction.Changes(900315, 1200393);
        Assert.Equal((4131L, 3218L, 2, 28L, 5, 1, 1), (
            changes.ConfiguredPlayerDelta.TotalGold,
            changes.ConfiguredPlayerDelta.Xp,
            changes.ConfiguredPlayerDelta.Level,
            changes.ConfiguredPlayerDelta.JungleCs,
            changes.ConfiguredPlayerDelta.Kills,
            changes.ConfiguredPlayerDelta.Deaths,
            changes.ConfiguredPlayerDelta.Assists));
        Assert.Equal((2810L, 4037L, 3, 31L, 2, 1, 3), (
            changes.EnemyJunglerDelta!.TotalGold,
            changes.EnemyJunglerDelta.Xp,
            changes.EnemyJunglerDelta.Level,
            changes.EnemyJunglerDelta.JungleCs,
            changes.EnemyJunglerDelta.Kills,
            changes.EnemyJunglerDelta.Deaths,
            changes.EnemyJunglerDelta.Assists));
        Assert.All(changes.Events, x => Assert.True(x.TimestampMs > 900315 && x.TimestampMs <= 1200393));
    }

    [Fact]
    public void MalformedChampionKillCoreFieldFailsButMalformedOptionalFieldIsPartial()
    {
        var (match, timeline) = FixtureJson();
        var root = JsonNode.Parse(timeline)!.AsObject();
        var kill = Events(root).First(x => x!["type"]!.GetValue<string>() == "CHAMPION_KILL")!.AsObject();
        kill["victimId"] = "bad";

        var exception = Assert.Throws<ReconstructionMappingException>(() => _mapper.Map(match, root.ToJsonString(), MatchId, ConfiguredPuuid));
        Assert.Equal("champion_kill_victim_invalid", exception.Code);

        (match, timeline) = FixtureJson();
        root = JsonNode.Parse(timeline)!.AsObject();
        kill = Events(root).First(x => x!["type"]!.GetValue<string>() == "CHAMPION_KILL")!.AsObject();
        kill["bounty"] = "bad";
        var reconstruction = new GameReconstruction(_mapper.Map(match, root.ToJsonString(), MatchId, ConfiguredPuuid));
        Assert.Contains(reconstruction.SourceDataIssues, x => x.Code == "event_field_invalid" && x.HandlingOutcome == "partial event retained");
    }

    [Fact]
    public void MalformedNonCriticalEventsArePartialOrSkippedWithoutInvalidatingKda()
    {
        var (match, timeline) = FixtureJson();
        var root = JsonNode.Parse(timeline)!.AsObject();
        var ward = Events(root).First(x => x!["type"]!.GetValue<string>() == "WARD_PLACED")!.AsObject();
        ward["timestamp"] = "bad";
        var item = Events(root).First(x => x!["type"]!.GetValue<string>() == "ITEM_PURCHASED")!.AsObject();
        item["participantId"] = "bad";
        var undo = Events(root).First(x => x!["type"]!.GetValue<string>() == "ITEM_UNDO")!.AsObject();
        undo["goldGain"] = "bad";

        var reconstruction = new GameReconstruction(_mapper.Map(match, root.ToJsonString(), MatchId, ConfiguredPuuid));

        Assert.Contains(reconstruction.SourceDataIssues, x => x.Code == "event_timestamp_invalid" && x.HandlingOutcome == "event omitted");
        Assert.Contains(reconstruction.SourceDataIssues, x => x.Code == "event_identity_invalid" && x.HandlingOutcome == "event omitted");
        Assert.Contains(reconstruction.SourceDataIssues, x => x.Code == "event_field_invalid" && x.HandlingOutcome == "partial event retained");
        Assert.Equal((16, 6, 3), Kda(reconstruction.StateAt(reconstruction.AvailableToMs).ConfiguredPlayer));
    }

    [Fact]
    public void UnknownEventTypeIsReportedAndMissingTypeFails()
    {
        var (match, timeline) = FixtureJson();
        var root = JsonNode.Parse(timeline)!.AsObject();
        Events(root)[0]!["type"] = "FUTURE_EVENT";
        var input = _mapper.Map(match, root.ToJsonString(), MatchId, ConfiguredPuuid);
        Assert.Contains(input.SourceDataIssues, x => x.Code == "unsupported_event_type" && x.Explanation.Contains("FUTURE_EVENT"));

        root = JsonNode.Parse(timeline)!.AsObject();
        Events(root)[0]!.AsObject().Remove("type");
        Assert.Equal("event_type_missing", Assert.Throws<ReconstructionMappingException>(() => _mapper.Map(match, root.ToJsonString(), MatchId, ConfiguredPuuid)).Code);
    }

    [Fact]
    public void UnsupportedQueueAndConfiguredRoleFailExplicitly()
    {
        var (match, timeline) = FixtureJson();
        var root = JsonNode.Parse(match)!.AsObject();
        root["info"]!["queueId"] = 440;
        Assert.Equal("unsupported_queue", Assert.Throws<ReconstructionMappingException>(() => _mapper.Map(root.ToJsonString(), timeline, MatchId, ConfiguredPuuid)).Code);

        root = JsonNode.Parse(match)!.AsObject();
        root["info"]!["participants"]![1]!["teamPosition"] = "MIDDLE";
        Assert.Equal("unsupported_role", Assert.Throws<ReconstructionMappingException>(() => _mapper.Map(root.ToJsonString(), timeline, MatchId, ConfiguredPuuid)).Code);
    }

    [Fact]
    public void MissingOrAmbiguousEnemyRoleRemainsUsableWithoutHeuristics()
    {
        var (match, timeline) = FixtureJson();
        var missing = JsonNode.Parse(match)!.AsObject();
        missing["info"]!["participants"]![6]!["teamPosition"] = "";
        var missingReconstruction = new GameReconstruction(_mapper.Map(missing.ToJsonString(), timeline, MatchId, ConfiguredPuuid));
        Assert.Equal(EnemyResolutionStatus.Missing, missingReconstruction.EnemyResolution.Status);
        Assert.Null(missingReconstruction.StateAt(300065).EnemyJungler);

        var ambiguous = JsonNode.Parse(match)!.AsObject();
        ambiguous["info"]!["participants"]![5]!["teamPosition"] = "JUNGLE";
        var ambiguousReconstruction = new GameReconstruction(_mapper.Map(ambiguous.ToJsonString(), timeline, MatchId, ConfiguredPuuid));
        Assert.Equal(EnemyResolutionStatus.Ambiguous, ambiguousReconstruction.EnemyResolution.Status);
        Assert.Null(ambiguousReconstruction.StateAt(300065).EnemyJungler);
    }

    [Fact]
    public void FixtureIdentityAndAbsoluteTimeAreSyntheticWhileRelativeEvidenceIsIntact()
    {
        var (matchJson, timelineJson) = FixtureJson();
        using var match = JsonDocument.Parse(matchJson);
        using var timeline = JsonDocument.Parse(timelineJson);

        Assert.Equal("EUW1_1", match.RootElement.GetProperty("metadata").GetProperty("matchId").GetString());
        Assert.Equal(1, match.RootElement.GetProperty("info").GetProperty("gameId").GetInt64());
        Assert.Equal(1788220800000, match.RootElement.GetProperty("info").GetProperty("gameStartTimestamp").GetInt64());
        Assert.Equal("participant-2-puuid", match.RootElement.GetProperty("info").GetProperty("participants")[1].GetProperty("puuid").GetString());
        Assert.Equal(300065, timeline.RootElement.GetProperty("info").GetProperty("frames")[5].GetProperty("timestamp").GetInt64());
        Assert.Equal(1691676, timeline.RootElement.GetProperty("info").GetProperty("frames")[29].GetProperty("timestamp").GetInt64());
    }

    private GameReconstruction MapFixture()
    {
        var (match, timeline) = FixtureJson();
        return new(_mapper.Map(match, timeline, MatchId, ConfiguredPuuid));
    }

    private static (string Match, string Timeline) FixtureJson() => (
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "match.json")),
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "timeline.json")));

    private static List<JsonNode?> Events(JsonObject timeline) => timeline["info"]!["frames"]!.AsArray().SelectMany(x => x!["events"]!.AsArray()).ToList();

    private static (int, int, int) Kda(PlayerState player) => (player.Kills, player.Deaths, player.Assists);

    private static void AssertPlayer(PlayerState player, long gold, long xp, int level, long cs, int kills, int deaths, int assists)
    {
        Assert.Equal(gold, player.Observation.TotalGold);
        Assert.Equal(xp, player.Observation.Xp);
        Assert.Equal(level, player.Observation.Level);
        Assert.Equal(cs, player.Observation.JungleCs);
        Assert.Equal((kills, deaths, assists), Kda(player));
    }
}
