using Pathwise.Domain.Encounters;
using Pathwise.Domain.Reconstruction;
using Pathwise.Domain.ReviewWindows;

namespace Pathwise.Domain.Tests;

public sealed class EncounterDetectorTests
{
    [Theory]
    [InlineData(5_000, 2_500, 2, 3, true, EncounterLinkTier.Local)]
    [InlineData(5_001, 2_500, 2, 3, true, EncounterLinkTier.Extended)]
    [InlineData(20_000, 2_500, 2, 3, true, EncounterLinkTier.Extended)]
    [InlineData(20_001, 2_500, 2, 3, false, EncounterLinkTier.Extended)]
    [InlineData(5_000, 2_501, 2, 3, false, EncounterLinkTier.Local)]
    [InlineData(5_000, 2_500, 2, 4, true, EncounterLinkTier.Local)]
    public void PositionedLinksRespectInclusiveThresholds(long gap, int distance, int firstVictim, int secondVictim, bool grouped, EncounterLinkTier tier)
    {
        var events = new[] { Kill(1_000, 1, 2, firstVictim, new(0, 0)), Kill(1_000 + gap, 2, 2, secondVictim, new(distance, 0)) };
        var encounters = Detect(events).Windows[0].Encounters;
        Assert.Equal(grouped ? 1 : 2, encounters.Count);
        if (grouped) Assert.Equal(tier, Assert.Single(encounters[0].GroupingEdges).Tier);
    }

    [Fact]
    public void LocalLinkNeedsNoOverlapAndExtendedLinkCountsRoleChangesAndDistinctAssists()
    {
        var local = Detect([Kill(1_000, 1, 2, 3, new(0, 0)), Kill(5_000, 2, 4, 5, new(1_000, 0))]);
        Assert.Single(local.Windows[0].Encounters);
        var roleChange = Detect([Kill(1_000, 1, 2, 3, new(0, 0), [4, 4]), Kill(15_000, 2, 4, 5, new(1_000, 0))]);
        Assert.Equal(EncounterLinkTier.Extended, Assert.Single(Assert.Single(roleChange.Windows[0].Encounters).GroupingEdges).Tier);
        Assert.Equal([2, 3, 4, 5], Assert.Single(roleChange.Windows[0].Encounters).ParticipantIds);
    }

    [Fact]
    public void FallbackRequiresTwoDistinctParticipantsAndCannotBridgePositionedComponents()
    {
        var left = Kill(1_000, 1, 2, 3, new(0, 0), [4, 4]);
        var missing = Kill(2_000, 2, 2, 3, null, [4]);
        var right = Kill(3_000, 3, 2, 3, new(10_000, 0), [4]);
        var encounters = Detect([right, missing, left]).Windows[0].Encounters;
        Assert.Equal(2, encounters.Count);
        Assert.Equal(3, encounters.Sum(e => e.CombatEventCount));
        Assert.All(encounters, encounter => Assert.True(encounter.CombatEvents.Where(e => e.Position is not null)
            .Select(e => e.Position!.X).Distinct().Count() <= 1));
        Assert.Equal(EncounterLinkTier.MissingPositionFallback, Assert.Single(encounters.Single(e => e.CombatEventCount == 2).GroupingEdges).Tier);
        var noOverlap = Detect([left, Kill(2_000, 2, 2, 5, null)]);
        Assert.Equal(2, noOverlap.Windows[0].Encounters.Count);
    }

    [Theory]
    [InlineData(10_000, true)]
    [InlineData(10_001, false)]
    public void MissingPositionFallbackUsesInclusiveTimeLimit(long gap, bool grouped)
    {
        var encounters = Detect([Kill(1_000, 1, 2, 3, null), Kill(1_000 + gap, 2, 2, 3, null)])
            .Windows[0].Encounters;
        Assert.Equal(grouped ? 1 : 2, encounters.Count);
        if (grouped) Assert.Equal(EncounterLinkTier.MissingPositionFallback, Assert.Single(encounters[0].GroupingEdges).Tier);
    }

    [Fact]
    public void EqualTimeRemoteEventsRemainDistinct()
    {
        var encounters = Detect([Kill(1_000, 1, 2, 3, new(0, 0)), Kill(1_000, 2, 2, 3, new(10_000, 0))])
            .Windows[0].Encounters;
        Assert.Equal(2, encounters.Count);
        Assert.Equal([1, 2], encounters.Select(e => e.CombatEvents[0].Source.FrameIndex));
    }

    [Fact]
    public void ComponentBoundsPreventSpatialAndTemporalChains()
    {
        var spatial = Enumerable.Range(0, 5).Select(i => Kill(1_000 + i * 4_000, i + 1, 2, 3, new(i * 2_000, 0))).ToArray();
        Assert.True(Detect(spatial).Windows[0].Encounters.Count > 1);
        var temporal = Enumerable.Range(0, 5).Select(i => Kill(1_000 + i * 20_000, i + 1, 2, 3, new(0, 0))).ToArray();
        Assert.True(Detect(temporal).Windows[0].Encounters.Count > 1);
        Assert.All(Detect(spatial).Windows[0].Encounters, encounter => Assert.True(encounter.CombatEvents.Max(e => e.Position!.X) - encounter.CombatEvents.Min(e => e.Position!.X) <= 5_000));
        Assert.All(Detect(temporal).Windows[0].Encounters, encounter => Assert.True(encounter.RecordedEventSpanMs <= 60_000));
    }

    [Fact]
    public void LaterEdgeReconcilesGroupsAndInputOrderDoesNotChangeIdentity()
    {
        var events = new[]
        {
            Kill(1_000, 1, 2, 3, new(0, 0)),
            Kill(9_000, 2, 4, 5, new(4_000, 0)),
            Kill(12_000, 3, 3, 4, new(2_000, 0))
        };
        var forward = Assert.Single(Detect(events).Windows[0].Encounters);
        var reverse = Assert.Single(Detect(events.Reverse().ToArray()).Windows[0].Encounters);
        Assert.Equal(forward.Id, reverse.Id);
        Assert.Equal(3, forward.CombatEventCount);
        Assert.Equal(2, forward.GroupingEdges.Count);
    }

    [Fact]
    public void SourceDeduplicationBoundariesUnknownKillerAndSingletons()
    {
        var before = Kill(0, 1, 99, 3, new(0, 0));
        var inside = Kill(1, 2, 99, 3, null);
        var end = Kill(100_000, 3, 99, 4, null);
        var beyond = Kill(100_001, 4, 99, 5, null);
        var encounters = Detect([before, inside, inside, end, beyond]).Windows[0].Encounters;
        Assert.Equal(2, encounters.Count);
        Assert.Equal([3], encounters[0].ParticipantIds);
        Assert.All(encounters, e => Assert.Equal(0, e.RecordedEventSpanMs));
        Assert.Empty(Detect([]).Windows[0].Encounters);
    }

    [Fact]
    public void ObjectiveAssociationIsNonExclusiveAndRequiresPositionAndThresholds()
    {
        var events = new ReconstructionEvent[]
        {
            Kill(1_000, 1, 2, 3, new(0, 0)),
            Kill(40_000, 2, 4, 5, new(0, 0)),
            Objective(20_000, 3, new(2_500, 0)),
            Objective(20_000, 4, null),
            Objective(71_000, 5, new(0, 0))
        };
        var encounters = Detect(events).Windows[0].Encounters;
        Assert.Equal(2, encounters.Count);
        Assert.All(encounters, e => Assert.Equal([new SourceEventReference(3, 0)], e.AssociatedObjectiveEvents.Select(o => o.Source)));
    }

    [Theory]
    [InlineData(30_000, 2_500, true)]
    [InlineData(30_001, 2_500, false)]
    [InlineData(30_000, 2_501, false)]
    public void ObjectiveAssociationUsesInclusiveTimeAndDistance(long gap, int distance, bool associated)
    {
        var encounter = Assert.Single(Detect([Kill(1_000, 1, 2, 3, new(0, 0)),
            Objective(1_000 + gap, 2, new(distance, 0))]).Windows[0].Encounters);
        Assert.Equal(associated ? 1 : 0, encounter.AssociatedObjectiveEvents.Count);
    }

    private static EncounterDetectionResult Detect(IReadOnlyList<ReconstructionEvent> events)
    {
        var participants = Enumerable.Range(1, 5).Select(i => new Participant(i, i < 4 ? 100 : 200, i, $"Champion{i}", "JUNGLE", false, 0, 0, 0)).ToArray();
        var observation = new PlayerObservation(2, null, 0, 0, 0, 1, 0, 0, null, null, null, null);
        var reconstruction = new GameReconstruction(new("TEST", "1.0", 420, 11, 100,
            participants, 2, new(EnemyResolutionStatus.Missing, null),
            [new(0, 0, observation, null), new(1, 100_000, observation, null)], events, []));
        var candidate = new ReviewWindowCandidate(1, ReviewSelectionReason.ConfiguredPlayerDeath, 0, 100_000,
            participants[1], null, reconstruction.EnemyResolution, reconstruction.Changes(0, 100_000), null,
            [], [], [], [], [], []);
        var windows = new ReviewWindowDetectionResult([candidate], 2, 1, ReviewWindowOptions.Default,
            reconstruction.EnemyResolution, [], [], [], []);
        return new EncounterDetector().Detect(reconstruction, windows);
    }

    private static ChampionKillEvent Kill(long time, int frame, int killer, int victim, Position? position, IReadOnlyList<int>? assists = null)
        => new(time, new(frame, 0), killer, victim, assists ?? [], position, null, null);

    private static EliteMonsterKillEvent Objective(long time, int frame, Position? position)
        => new(time, new(frame, 0), "BARON_NASHOR", null, 2,
            new(TeamAttributionKind.KnownTeam, 100, 100, null), [], position, null);
}
