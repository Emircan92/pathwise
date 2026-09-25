using Pathwise.Domain.FactualObservations;
using Pathwise.Domain.Reconstruction;

namespace Pathwise.Domain.Encounters;

public enum EncounterLinkTier { Local, Extended, MissingPositionFallback }

public sealed record EncounterPolicy(
    long LocalTimeMs, long ExtendedTimeMs, long MissingPositionTimeMs,
    long LinkDistanceUnits, int ExtendedOverlap, int MissingPositionOverlap,
    long MaximumSpanMs, long MaximumDiameterUnits,
    long ObjectiveTimeMs, long ObjectiveDistanceUnits);

public sealed record EncounterGroupingEdge(
    SourceEventReference Earlier, SourceEventReference Later, EncounterLinkTier Tier);

public sealed record EncounterPlayerSummary(bool Involved, int Kills, int Deaths, int Assists, int DistinctEventCount);

public sealed record Encounter(
    string Id, long StartTimestampMs, long EndTimestampMs, long RecordedEventSpanMs,
    int CombatEventCount, IReadOnlyList<int> ParticipantIds, int DistinctParticipantCount,
    EncounterPlayerSummary ConfiguredPlayerSummary, bool? EnemyJunglerInvolved,
    IReadOnlyList<ChampionKillEvent> CombatEvents,
    IReadOnlyList<EliteMonsterKillEvent> AssociatedObjectiveEvents,
    IReadOnlyList<EncounterGroupingEdge> GroupingEdges);

public sealed record WindowEncounters(SelectedReviewWindowKey Window, IReadOnlyList<Encounter> Encounters);

public sealed record EncounterDetectionResult(
    int DetectorVersion, EncounterPolicy EffectivePolicy, IReadOnlyList<WindowEncounters> Windows);
