using System.Text.Json;
using System.Text.Json.Nodes;
using Pathwise.Domain.Reconstruction;
using Pathwise.Infrastructure.Reconstruction;

namespace Pathwise.Infrastructure.Tests;

public sealed class RiotReconstructionMapperTests
{
    private const string MatchId = "EUW1_1";
    private const string ConfiguredPuuid = "participant-2-puuid";
    private readonly RiotReconstructionMapper _mapper = new();

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
