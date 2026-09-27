using Pathwise.Domain.Encounters;
using Pathwise.Domain.Progression;
using Pathwise.Domain.Reconstruction;
using Pathwise.Domain.ReviewWindows;
using Pathwise.Infrastructure.Reconstruction;

namespace Pathwise.Infrastructure.Tests;

public sealed class ProgressionCalibrationFixtureTests
{
    private readonly RiotReconstructionMapper _mapper = new();

    [Fact]
    public void FixturePreservesMatchSummaryAndLateProgressionWithProvenance()
    {
        var reconstruction = MapFixture();
        var windows = new ReviewWindowDetector().Detect(reconstruction, ReviewWindowOptions.Default);
        var progression = new ProgressionEvidenceProjector().Project(reconstruction, windows);
        var encounters = new EncounterDetector().Detect(reconstruction, windows);

        Assert.Equal(100, progression.Outcome.ResolvedWinningTeamId);
        Assert.True(progression.Outcome.ConfiguredPlayerWon!.Value);
        Assert.Equal("match.info.participants[1].win", progression.Outcome.ConfiguredPlayerWon.Source.JsonPath);
        Assert.Equal(1_286, progression.Outcome.ReportedDurationSeconds.Value);
        Assert.Equal("GameComplete", progression.Outcome.EndOfGameResult!.Value);
        Assert.Equal("match.info.gameEndTimestamp", progression.Outcome.MatchEndTimestampMs!.Source.JsonPath);

        var configuredSummary = progression.Outcome.ParticipantResults.Single(value => value.ParticipantId == 2);
        Assert.Equal((1, 1, 0), (
            configuredSummary.NexusKills!.Value,
            configuredSummary.NexusTakedowns!.Value,
            configuredSummary.NexusLost!.Value));
        Assert.Equal("match.info.participants[1].nexusKills", configuredSummary.NexusKills.Source.JsonPath);
        Assert.All(progression.Outcome.ParticipantResults.Where(value => value.TeamId == 200), value =>
            Assert.Equal(1, value.NexusLost!.Value));

        var gameEnd = Assert.IsType<GameEndedProgressionEvent>(progression.Outcome.TimelineGameEnd);
        Assert.Equal((1_286_891L, 22, 11, 100), (
            gameEnd.TimestampMs,
            gameEnd.Source.FrameIndex,
            gameEnd.Source.EventIndex,
            gameEnd.WinningTeam.ResolvedTeamId));

        var lateWindow = Assert.Single(progression.Windows, value => value.ContainsGameEnd);
        var relevant = lateWindow.Events.Where(value => value is GameEndedProgressionEvent ||
            value is BuildingDestroyedProgressionEvent building && building.StructureOwnerTeam.ResolvedTeamId == 200).ToArray();
        Assert.Collection(relevant,
            value => Building(value, 1_206_286, 21, 3, "BASE_TURRET", "MID_LANE"),
            value => Building(value, 1_213_961, 21, 7, null, "MID_LANE"),
            value => Building(value, 1_221_794, 21, 15, "BASE_TURRET", "TOP_LANE"),
            value => Building(value, 1_228_277, 21, 20, null, "TOP_LANE"),
            value => Building(value, 1_257_369, 21, 45, "NEXUS_TURRET", "MID_LANE"),
            value => Building(value, 1_264_618, 22, 0, "NEXUS_TURRET", "MID_LANE"),
            value => Assert.IsType<GameEndedProgressionEvent>(value));

        var lateEncounters = encounters.Windows.Single(value =>
            value.Window.RequestedStartTimestampMs == lateWindow.Window.RequestedStartTimestampMs &&
            value.Window.RequestedEndTimestampMs == lateWindow.Window.RequestedEndTimestampMs).Encounters;
        Assert.Equal(2, lateEncounters.Count);
        Assert.Equal([(1_220_377L, 1_245_616L), (1_271_834L, 1_278_152L)],
            lateEncounters.Select(value => (value.StartTimestampMs, value.EndTimestampMs)).ToArray());
    }

    [Fact]
    public void FixtureKeepsDragonTeamAttributionIndependentFromCrossTeamAssist()
    {
        var reconstruction = MapFixture();
        var dragon = Assert.Single(reconstruction.Events.OfType<EliteMonsterKillEvent>(), value =>
            value.TimestampMs == 1_054_897);

        Assert.Equal(7, dragon.KillerParticipantId);
        Assert.Equal(200, dragon.TeamAttribution.ResolvedTeamId);
        Assert.Equal([2, 6, 9], dragon.AssistingParticipantIds);
        Assert.Equal(100, reconstruction.Participants.Single(value => value.ParticipantId == 2).TeamId);
    }

    [Fact]
    public void FixtureKeepsHeraldKillAndItemDestructionAsSeparateFacts()
    {
        var reconstruction = MapFixture();
        var progression = new ProgressionEvidenceProjector().Project(
            reconstruction,
            new ReviewWindowDetector().Detect(reconstruction, ReviewWindowOptions.Default));

        var herald = Assert.Single(progression.Events.OfType<RiftHeraldKilledProgressionEvent>());
        var item = Assert.Single(progression.Events.OfType<ItemDestroyedProgressionEvent>());
        Assert.Equal((982_051L, 2, 100), (herald.TimestampMs, herald.KillerParticipantId, herald.TeamAttribution.ResolvedTeamId));
        Assert.Equal((1_194_580L, 2, 3513), (item.TimestampMs, item.ParticipantId, item.ItemId));
        Assert.DoesNotContain(progression.Events, value => value.GetType().Name.Contains("Summon", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(progression.Events, value => value.GetType().Name.Contains("Charge", StringComparison.OrdinalIgnoreCase));
    }

    private GameReconstruction MapFixture()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Progression");
        var input = _mapper.Map(
            File.ReadAllText(Path.Combine(directory, "match.json")),
            File.ReadAllText(Path.Combine(directory, "timeline.json")),
            "EUW1_2",
            "participant-2-puuid");
        return new(input);
    }

    private static void Building(
        ProgressionEvent value,
        long timestamp,
        int frame,
        int index,
        string? towerType,
        string laneType)
    {
        var building = Assert.IsType<BuildingDestroyedProgressionEvent>(value);
        Assert.Equal((timestamp, frame, index, towerType, laneType, 200), (
            building.TimestampMs,
            building.Source.FrameIndex,
            building.Source.EventIndex,
            building.TowerType,
            building.LaneType,
            building.StructureOwnerTeam.ResolvedTeamId));
    }
}
