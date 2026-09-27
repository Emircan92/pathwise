using Pathwise.Domain.Progression;
using Pathwise.Domain.Reconstruction;
using Pathwise.Domain.ReviewWindows;

namespace Pathwise.Domain.Tests;

public sealed class ProgressionEvidenceProjectorTests
{
    [Fact]
    public void ProjectsSupportedFactsInStableOrderAndUsesExistingWindowBoundaries()
    {
        var events = new ReconstructionEvent[]
        {
            Building(200, 2, 4, "INHIBITOR_BUILDING", null, "MID_LANE", 200, null),
            new ItemTransactionEvent(150, new(1, 3), 2, "destroyed", 3513, null, null, null),
            Building(100, 1, 2, null, null, null, 200, null),
            new EliteMonsterKillEvent(125, new(1, 1), "RIFTHERALD", null, 2,
                Team(100), [7], new(4_738, 9_924), 0),
            new GameEndEvent(200, new(2, 5), Team(100)),
            new ItemTransactionEvent(175, new(1, 4), 2, "destroyed", 1001, null, null, null),
            new StructureKillEvent(160, new(1, 5), "TOWER_BUILDING", "OUTER_TURRET", "TOP_LANE",
                Team(200), 2, Team(100), [], null, 0, true)
        };
        var reconstruction = Reconstruction(events, Summary());
        var windows = Windows(reconstruction, 100, 200);

        var result = new ProgressionEvidenceProjector().Project(reconstruction, windows);

        Assert.Equal(1, result.ProjectorVersion);
        Assert.Collection(result.Events,
            value => Assert.IsType<BuildingDestroyedProgressionEvent>(value),
            value => Assert.IsType<RiftHeraldKilledProgressionEvent>(value),
            value => Assert.IsType<ItemDestroyedProgressionEvent>(value),
            value => Assert.IsType<BuildingDestroyedProgressionEvent>(value),
            value => Assert.IsType<GameEndedProgressionEvent>(value));
        Assert.Equal([100L, 125L, 150L, 200L, 200L], result.Events.Select(value => value.TimestampMs));
        Assert.True(Assert.Single(result.Windows).ContainsGameEnd);
        Assert.Equal([125L, 150L, 200L, 200L], result.Windows[0].Events.Select(value => value.TimestampMs));

        var inhibitor = Assert.IsType<BuildingDestroyedProgressionEvent>(result.Events[3]);
        Assert.Equal("INHIBITOR_BUILDING", inhibitor.BuildingType);
        Assert.Null(inhibitor.TowerType);
        Assert.Null(inhibitor.KillerParticipantId);
        Assert.Equal(200, inhibitor.StructureOwnerTeam.ResolvedTeamId);
        Assert.Equal(new SourceEventReference(2, 4), inhibitor.Source);

        var item = Assert.IsType<ItemDestroyedProgressionEvent>(result.Events[2]);
        Assert.Equal(3513, item.ItemId);
        Assert.DoesNotContain("summon", item.GetType().Name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void KeepsCrossTeamAssistsSeparateFromObjectiveTeamAttribution()
    {
        var herald = new EliteMonsterKillEvent(125, new(1, 1), "RIFTHERALD", null, 7,
            Team(200), [2, 8], null, 0);
        var reconstruction = Reconstruction([herald], Summary());

        var result = new ProgressionEvidenceProjector().Project(reconstruction, Windows(reconstruction, 0, 200));

        var projected = Assert.IsType<RiftHeraldKilledProgressionEvent>(Assert.Single(result.Events));
        Assert.Equal(200, projected.TeamAttribution.ResolvedTeamId);
        Assert.Equal([2, 8], projected.AssistingParticipantIds);
        Assert.Equal(100, reconstruction.Participants.Single(value => value.ParticipantId == 2).TeamId);
    }

    [Fact]
    public void LeavesResolvedWinnerUnsetWhenSummaryAndTimelineConflict()
    {
        var summary = Summary(team100Won: false);
        var gameEnd = new GameEndEvent(200, new(2, 5), Team(100));
        var reconstruction = Reconstruction([gameEnd], summary);

        var result = new ProgressionEvidenceProjector().Project(reconstruction, Windows(reconstruction, 0, 200));

        Assert.Null(result.Outcome.ResolvedWinningTeamId);
        Assert.Equal(100, result.Outcome.TimelineGameEnd!.WinningTeam.ResolvedTeamId);
        Assert.False(result.Outcome.TeamResults.Single(value => value.TeamId == 100).Won.Value);
    }

    [Fact]
    public void GameEndOutsideWindowDoesNotSetContainsGameEnd()
    {
        var reconstruction = Reconstruction(
            [new GameEndEvent(201, new(2, 5), Team(100))],
            Summary());

        var result = new ProgressionEvidenceProjector().Project(reconstruction, Windows(reconstruction, 100, 200));

        Assert.False(Assert.Single(result.Windows).ContainsGameEnd);
        Assert.Empty(result.Windows[0].Events);
        Assert.Single(result.Events);
    }

    private static GameReconstruction Reconstruction(
        IReadOnlyList<ReconstructionEvent> events,
        MatchSummaryEvidence summary)
    {
        var participants = new[]
        {
            new Participant(2, 100, 121, "Kha'Zix", "JUNGLE", true, 0, 0, 0),
            new Participant(7, 200, 35, "Shaco", "JUNGLE", false, 0, 0, 0),
            new Participant(8, 200, 1, "Annie", "MIDDLE", false, 0, 0, 0)
        };
        var observations = new[]
        {
            Frame(0, 0),
            Frame(1, 100),
            Frame(2, 200),
            Frame(3, 300)
        };
        return new(new(
            "EUW1_TEST", "16.18", 420, 11, 300, participants, 2,
            new(EnemyResolutionStatus.Resolved, 7), observations, events, [], summary));
    }

    private static FrameObservation Frame(int index, long timestamp) => new(
        index,
        timestamp,
        new(2, null, 0, 0, 0, 1, 0, 0, null, null, null, null),
        new(7, null, 0, 0, 0, 1, 0, 0, null, null, null, null));

    private static MatchSummaryEvidence Summary(bool team100Won = true) => new(
        new(300, new("match.info.gameDuration")),
        new(1_000_000, new("match.info.gameEndTimestamp")),
        new("GameComplete", new("match.info.endOfGameResult")),
        [
            new(100, new(team100Won, new("match.info.teams[0].win"))),
            new(200, new(!team100Won, new("match.info.teams[1].win")))
        ],
        [
            new(2, 100, new(team100Won, new("match.info.participants[1].win")),
                new(false, new("match.info.participants[1].gameEndedInSurrender")), null,
                new(1, new("match.info.participants[1].nexusKills")),
                new(1, new("match.info.participants[1].nexusTakedowns")),
                new(0, new("match.info.participants[1].nexusLost"))),
            new(7, 200, new(!team100Won, new("match.info.participants[6].win")),
                new(false, new("match.info.participants[6].gameEndedInSurrender")), null,
                new(0, new("match.info.participants[6].nexusKills")),
                new(0, new("match.info.participants[6].nexusTakedowns")),
                new(1, new("match.info.participants[6].nexusLost")))
        ]);

    private static ReviewWindowDetectionResult Windows(GameReconstruction reconstruction, long start, long end)
    {
        var configured = reconstruction.Participants.Single(value => value.ParticipantId == 2);
        var enemy = reconstruction.Participants.Single(value => value.ParticipantId == 7);
        var candidate = new ReviewWindowCandidate(
            1, ReviewSelectionReason.ConfiguredPlayerDeath, start, end, configured, enemy,
            reconstruction.EnemyResolution, reconstruction.Changes(start, end), null,
            [], [], [], reconstruction.EventsBetween(start, end), reconstruction.SourceDataIssues, []);
        return new(
            [candidate], 2, reconstruction.ReconstructionVersion, ReviewWindowOptions.Default,
            reconstruction.EnemyResolution, reconstruction.SourceDataIssues, [], [], []);
    }

    private static StructureKillEvent Building(
        long timestamp,
        int frame,
        int index,
        string? building,
        string? tower,
        string? lane,
        int owner,
        int? killer) => new(
            timestamp, new(frame, index), building, tower, lane, Team(owner), killer,
            killer is null ? new(TeamAttributionKind.Unknown, null, null, null) : Team(100),
            [], null, 0, false);

    private static TeamAttribution Team(int teamId) =>
        new(TeamAttributionKind.KnownTeam, teamId, teamId, null);
}
