using Pathwise.Domain.Reconstruction;
using Pathwise.Domain.ReviewWindows;

namespace Pathwise.Domain.FactualObservations;

public enum FactualObservationKind
{
    RelativeGoldMovement,
    RelativeXpMovement,
    RelativeJungleCsMovement,
    ConfiguredPlayerCombat,
    EliteObjectiveContext
}

public sealed record SelectedReviewWindowKey(
    string MatchId,
    int ConfiguredParticipantId,
    int? EnemyParticipantId,
    long RequestedStartTimestampMs,
    long RequestedEndTimestampMs,
    int ReconstructionVersion,
    int DetectorVersion);

public sealed record FactualObservationKey(
    SelectedReviewWindowKey Window,
    int GeneratorVersion,
    FactualObservationKind Kind);

public sealed record ParticipantMetricEndpoints(
    int ParticipantId,
    long StartValue,
    long EndValue)
{
    public long Change => EndValue - StartValue;
}

public sealed record RelativeMetricEvidence(
    SourceFrameReference StartFrame,
    SourceFrameReference EndFrame,
    ParticipantMetricEndpoints ConfiguredPlayer,
    ParticipantMetricEndpoints EnemyJungler)
{
    public long RelativeStart => ConfiguredPlayer.StartValue - EnemyJungler.StartValue;
    public long RelativeEnd => ConfiguredPlayer.EndValue - EnemyJungler.EndValue;
    public long SignedChange => RelativeEnd - RelativeStart;
}

public abstract record FactualObservation(FactualObservationKey Key);

public sealed record RelativeGoldObservation(
    FactualObservationKey Key,
    RelativeMetricEvidence Evidence)
    : FactualObservation(Key);

public sealed record RelativeXpObservation(
    FactualObservationKey Key,
    RelativeMetricEvidence Evidence)
    : FactualObservation(Key);

public sealed record RelativeJungleCsObservation(
    FactualObservationKey Key,
    RelativeMetricEvidence Evidence)
    : FactualObservation(Key);

public sealed record ConfiguredPlayerCombatObservation(
    FactualObservationKey Key,
    IReadOnlyList<ChampionKillEvent> Events)
    : FactualObservation(Key)
{
    public int KillCount => Events.Count(
        e => e.KillerParticipantId == Key.Window.ConfiguredParticipantId);

    public int DeathCount => Events.Count(
        e => e.VictimParticipantId == Key.Window.ConfiguredParticipantId);

    public int AssistCount => Events.Count(
        e => e.AssistingParticipantIds.Contains(
            Key.Window.ConfiguredParticipantId));

    public int DistinctEventCount => Events.Count;
}

public sealed record EliteObjectiveContextObservation(
    FactualObservationKey Key,
    IReadOnlyList<EliteMonsterKillEvent> Events)
    : FactualObservation(Key);

public sealed record FactualObservationPolicy(
    long MinimumAbsoluteGoldChange,
    long MinimumAbsoluteXpChange,
    long MinimumAbsoluteJungleCsChange);

public sealed record CounterRegressionEvidence(
    int ParticipantId,
    SourceFrameReference PreviousFrame,
    SourceFrameReference CurrentFrame,
    long PreviousValue,
    long CurrentValue);

public sealed record MetricObservationOmission(
    FactualObservationKind Kind,
    CounterRegressionEvidence Regression);

public sealed record WindowFactualObservations(
    SelectedReviewWindowKey Window,
    EnemyJunglerResolution EnemyResolution,
    IReadOnlyList<FactualObservation> Observations,
    IReadOnlyList<MetricObservationOmission> Omissions,
    IReadOnlyList<SourceDataIssue> SourceIssues);

public sealed record FactualObservationResult(
    int GeneratorVersion,
    FactualObservationPolicy EffectivePolicy,
    IReadOnlyList<WindowFactualObservations> Windows);
