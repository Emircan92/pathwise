using Pathwise.Domain.Reconstruction;

namespace Pathwise.Domain.Tests;

public sealed class GameReconstructionTests
{
    [Fact]
    public void StateAtUsesFloorFrameWithoutInterpolationAndIncludesEventsAtCutoff()
    {
        var reconstruction = CreateReconstruction();

        var state = reconstruction.StateAt(150);

        Assert.Equal(0, state.SelectedFrameIndex);
        Assert.Equal(100, state.SelectedFrameTimestampMs);
        Assert.Equal(1000, state.ConfiguredPlayer.Observation.TotalGold);
        Assert.Equal(1, state.ConfiguredPlayer.Kills);
        Assert.Equal(1, state.ConfiguredPlayer.Assists);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(301)]
    [InlineData(-1)]
    public void StateAtRejectsTimestampsOutsideFrameCoverage(long timestamp)
    {
        var exception = Assert.Throws<ReconstructionQueryException>(() => CreateReconstruction().StateAt(timestamp));
        Assert.Equal(100, exception.AvailableFromMs);
        Assert.Equal(300, exception.AvailableToMs);
    }

    [Fact]
    public void ChangesUsesOpenClosedEventIntervalAndSignedDeltas()
    {
        var changes = CreateReconstruction().Changes(150, 300);

        Assert.Equal(2, changes.Events.Count);
        Assert.DoesNotContain(changes.Events, x => x.TimestampMs == 150);
        Assert.Contains(changes.Events, x => x.TimestampMs == 300);
        Assert.Equal(500, changes.ConfiguredPlayerDelta.TotalGold);
        Assert.Equal(200, changes.ConfiguredPlayerDelta.Xp);
        Assert.Equal(1, changes.ConfiguredPlayerDelta.Level);
        Assert.Equal(10, changes.ConfiguredPlayerDelta.JungleCs);
        Assert.Equal(1, changes.ConfiguredPlayerDelta.Kills);
        Assert.Equal(1, changes.ConfiguredPlayerDelta.Deaths);
        Assert.Equal(0, changes.ConfiguredPlayerDelta.Assists);
    }

    [Fact]
    public void EqualEndpointsHaveZeroKnownDeltasNullUnknownDeltasAndNoEvents()
    {
        var changes = CreateReconstruction(missingHealth: true).Changes(200, 200);

        Assert.Empty(changes.Events);
        Assert.Equal(0, changes.ConfiguredPlayerDelta.TotalGold);
        Assert.Equal(0, changes.ConfiguredPlayerDelta.Kills);
        Assert.Null(changes.ConfiguredPlayerDelta.CurrentHealth);
    }

    [Fact]
    public void ReversedIntervalsAreInvalid()
    {
        var exception = Assert.Throws<ReconstructionQueryException>(() => CreateReconstruction().Changes(300, 100));
        Assert.Equal("reversed_interval", exception.Code);
    }

    [Fact]
    public void EqualTimeEventsRemainDistinctAndUseSourceOrdering()
    {
        var events = CreateReconstruction().Events.Where(x => x.TimestampMs == 150).ToArray();

        Assert.Equal(2, events.Length);
        Assert.Equal(0, events[0].Source.EventIndex);
        Assert.Equal(1, events[1].Source.EventIndex);
    }

    [Fact]
    public void KdaUsesDistinctAssistsAndUnknownKillerStillCountsVictimDeath()
    {
        var state = CreateReconstruction().StateAt(300);

        Assert.Equal((2, 1, 1), (state.ConfiguredPlayer.Kills, state.ConfiguredPlayer.Deaths, state.ConfiguredPlayer.Assists));
        Assert.Equal(4, state.ConfiguredPlayer.CombatEventReferences.Count);
    }

    [Fact]
    public void MissingEnemyKeepsConfiguredPlayerStateUsable()
    {
        var reconstruction = CreateReconstruction(enemyStatus: EnemyResolutionStatus.Missing);

        var state = reconstruction.StateAt(200);

        Assert.Null(state.EnemyJungler);
        Assert.Equal(EnemyResolutionStatus.Missing, state.EnemyResolution.Status);
    }

    private static GameReconstruction CreateReconstruction(bool missingHealth = false, EnemyResolutionStatus enemyStatus = EnemyResolutionStatus.Resolved)
    {
        var configured = new Participant(2, 100, 121, "Khazix", "JUNGLE", false, 2, 1, 1);
        var enemy = new Participant(7, 200, 200, "Belveth", "JUNGLE", true, 0, 1, 0);
        PlayerObservation Observation(long gold, long xp, int level, long cs, long? health) =>
            new(2, new(10, 20), gold, 100, xp, level, cs, 1, health, missingHealth ? null : 1000, 200, 300);
        PlayerObservation EnemyObservation(long gold) =>
            new(7, new(30, 40), gold, 50, 800, 4, 15, 2, 900, 900, 100, 200);
        var includeEnemy = enemyStatus == EnemyResolutionStatus.Resolved;
        var observations = new[]
        {
            new FrameObservation(0, 100, Observation(1000, 900, 4, 20, missingHealth ? null : 800), includeEnemy ? EnemyObservation(900) : null),
            new FrameObservation(1, 200, Observation(1200, 1000, 5, 25, missingHealth ? null : 600), includeEnemy ? EnemyObservation(1000) : null),
            new FrameObservation(2, 300, Observation(1500, 1100, 5, 30, missingHealth ? null : 700), includeEnemy ? EnemyObservation(1100) : null)
        };
        var events = new ReconstructionEvent[]
        {
            new ChampionKillEvent(150, new(1, 1), 2, 7, Array.Empty<int>(), null, 300, 0),
            new ChampionKillEvent(150, new(1, 0), 7, 1, new[] { 2, 2 }, null, 300, 0),
            new ChampionKillEvent(250, new(2, 0), 99, 2, Array.Empty<int>(), null, 300, 0),
            new ChampionKillEvent(300, new(2, 1), 2, 7, Array.Empty<int>(), null, 300, 0)
        };
        return new(new(
            "EUW1_1", "1.0", 420, 11, 1, new[] { configured, enemy }, 2,
            new(enemyStatus, includeEnemy ? 7 : null), observations, events, Array.Empty<SourceDataIssue>()));
    }
}
