using System.Text.Json.Serialization;

namespace Pathwise.Application.Interpretation;

public static class NarrativeInterpretationVersions
{
    public const int Input = 1;
    public const int Output = 1;
    public const int PromptPolicy = 3;
}

public sealed record NarrativeInterpretationSelection(
    long RequestedStartTimestampMs,
    long RequestedEndTimestampMs,
    int ReconstructionVersion,
    int DetectorVersion);

public sealed record NarrativeUpstreamVersionsV1(
    int Reconstruction,
    int Detector,
    int FactualObservations,
    int KnowledgeAnnotations,
    int Encounters,
    int Progression);

public sealed record NarrativeParticipantV1(
    int ParticipantId,
    string ChampionName,
    int TeamId,
    string Relationship);

public sealed record NarrativeSourceFrameV1(int FrameIndex, long TimestampMs);
public sealed record NarrativeSourceEventV1(int FrameIndex, int EventIndex);
public sealed record NarrativeMetricEndpointsV1(long StartValue, long EndValue);
public sealed record NarrativeTeamAttributionV1(string Kind, int? SuppliedTeamId, int? ResolvedTeamId, string? DiagnosticReason);
public sealed record NarrativeKnowledgeSourceV1(string Title, string Url);

public sealed record NarrativeWindowV1(
    long RequestedStartTimestampMs,
    long RequestedEndTimestampMs,
    NarrativeSourceFrameV1 StartFrame,
    NarrativeSourceFrameV1 EndFrame,
    string PrimarySelectionReason,
    IReadOnlyList<string> SignalKinds,
    IReadOnlyList<string> AbsorbedSignalKinds);

public sealed record NarrativeKnowledgeContextV1(string? PublicPatch, string Coverage);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(NarrativeWindowSelectionEvidenceV1), "windowSelection")]
[JsonDerivedType(typeof(NarrativeMetricObservationEvidenceV1), "metricObservation")]
[JsonDerivedType(typeof(NarrativeCombatSummaryEvidenceV1), "configuredPlayerCombatSummary")]
[JsonDerivedType(typeof(NarrativeObjectiveSummaryEvidenceV1), "eliteObjectiveSummary")]
[JsonDerivedType(typeof(NarrativeCombatEventEvidenceV1), "combatEvent")]
[JsonDerivedType(typeof(NarrativeObjectiveEventEvidenceV1), "objectiveEvent")]
[JsonDerivedType(typeof(NarrativeEncounterEvidenceV1), "encounter")]
[JsonDerivedType(typeof(NarrativeBuildingDestroyedEvidenceV1), "buildingDestroyed")]
[JsonDerivedType(typeof(NarrativeRiftHeraldKilledEvidenceV1), "riftHeraldKilled")]
[JsonDerivedType(typeof(NarrativeItemDestroyedEvidenceV1), "itemDestroyed")]
[JsonDerivedType(typeof(NarrativeGameEndedEvidenceV1), "gameEnded")]
[JsonDerivedType(typeof(NarrativeKnowledgeAnnotationEvidenceV1), "knowledgeAnnotation")]
[JsonDerivedType(typeof(NarrativeMetricOmissionEvidenceV1), "metricOmission")]
[JsonDerivedType(typeof(NarrativeSourceIssueEvidenceV1), "sourceDataIssue")]
[JsonDerivedType(typeof(NarrativeOutcomeEvidenceV1), "gameOutcome")]
public abstract record NarrativeEvidenceV1(string Id);

public sealed record NarrativeWindowSelectionEvidenceV1(
    string Id,
    string PrimarySelectionReason,
    IReadOnlyList<string> SignalKinds,
    IReadOnlyList<string> AbsorbedSignalKinds) : NarrativeEvidenceV1(Id);

public sealed record NarrativeMetricObservationEvidenceV1(
    string Id,
    string Metric,
    NarrativeSourceFrameV1 StartFrame,
    NarrativeSourceFrameV1 EndFrame,
    long RelativeStart,
    long RelativeEnd,
    long SignedChange,
    NarrativeMetricEndpointsV1 ConfiguredPlayer,
    NarrativeMetricEndpointsV1 EnemyJungler) : NarrativeEvidenceV1(Id);

public sealed record NarrativeCombatSummaryEvidenceV1(
    string Id,
    int Kills,
    int Deaths,
    int Assists,
    int DistinctEventCount,
    IReadOnlyList<string> EventEvidenceIds) : NarrativeEvidenceV1(Id);

public sealed record NarrativeObjectiveSummaryEvidenceV1(
    string Id,
    IReadOnlyList<string> EventEvidenceIds) : NarrativeEvidenceV1(Id);

public sealed record NarrativeCombatEventEvidenceV1(
    string Id,
    NarrativeSourceEventV1 Source,
    long TimestampMs,
    int? KillerParticipantId,
    int VictimParticipantId,
    IReadOnlyList<int> AssistingParticipantIds) : NarrativeEvidenceV1(Id);

public sealed record NarrativeObjectiveEventEvidenceV1(
    string Id,
    NarrativeSourceEventV1 Source,
    long TimestampMs,
    string? MonsterType,
    string? MonsterSubType,
    int? KillerParticipantId,
    IReadOnlyList<int> AssistingParticipantIds,
    NarrativeTeamAttributionV1 TeamAttribution) : NarrativeEvidenceV1(Id);

public sealed record NarrativeEncounterPlayerSummaryV1(bool Involved, int Kills, int Deaths, int Assists, int DistinctEventCount);

public sealed record NarrativeEncounterEvidenceV1(
    string Id,
    string EncounterId,
    long StartTimestampMs,
    long EndTimestampMs,
    int CombatEventCount,
    IReadOnlyList<int> ParticipantIds,
    NarrativeEncounterPlayerSummaryV1 ConfiguredPlayerSummary,
    bool? EnemyJunglerInvolved,
    IReadOnlyList<string> CombatEventEvidenceIds,
    IReadOnlyList<string> AssociatedObjectiveEvidenceIds) : NarrativeEvidenceV1(Id);

public sealed record NarrativeBuildingDestroyedEvidenceV1(
    string Id,
    NarrativeSourceEventV1 Source,
    long TimestampMs,
    string? BuildingType,
    string? TowerType,
    string? LaneType,
    NarrativeTeamAttributionV1 StructureOwnerTeam,
    int? KillerParticipantId,
    IReadOnlyList<int> AssistingParticipantIds) : NarrativeEvidenceV1(Id);

public sealed record NarrativeRiftHeraldKilledEvidenceV1(
    string Id,
    NarrativeSourceEventV1 Source,
    long TimestampMs,
    int? KillerParticipantId,
    NarrativeTeamAttributionV1 TeamAttribution,
    IReadOnlyList<int> AssistingParticipantIds) : NarrativeEvidenceV1(Id);

public sealed record NarrativeItemDestroyedEvidenceV1(
    string Id,
    NarrativeSourceEventV1 Source,
    long TimestampMs,
    int? ParticipantId,
    int ItemId) : NarrativeEvidenceV1(Id);

public sealed record NarrativeGameEndedEvidenceV1(
    string Id,
    NarrativeSourceEventV1 Source,
    long TimestampMs,
    NarrativeTeamAttributionV1 WinningTeam) : NarrativeEvidenceV1(Id);

public sealed record NarrativeKnowledgeAnnotationEvidenceV1(
    string Id,
    string AnnotationKind,
    string FactId,
    string Objective,
    long InitialSpawnTimestampMs,
    string? TargetObservationKind,
    string? TargetEventEvidenceId,
    long? RecordedKillTimestampMs,
    IReadOnlyList<NarrativeKnowledgeSourceV1> Sources) : NarrativeEvidenceV1(Id);

public sealed record NarrativeMetricOmissionEvidenceV1(
    string Id,
    string Metric,
    string Reason,
    int ParticipantId,
    NarrativeSourceFrameV1 PreviousFrame,
    NarrativeSourceFrameV1 CurrentFrame,
    long PreviousValue,
    long CurrentValue) : NarrativeEvidenceV1(Id);

public sealed record NarrativeSourceIssueEvidenceV1(
    string Id,
    string Code,
    string SourceReference,
    string Explanation,
    string HandlingOutcome) : NarrativeEvidenceV1(Id);

public sealed record NarrativeMatchFieldV1<T>(T Value, string JsonPath);

public sealed record NarrativeOutcomeEvidenceV1(
    string Id,
    int ConfiguredParticipantId,
    int ConfiguredTeamId,
    NarrativeMatchFieldV1<bool>? ConfiguredPlayerWon,
    int? ResolvedWinningTeamId,
    NarrativeMatchFieldV1<string>? EndOfGameResult,
    NarrativeMatchFieldV1<long>? MatchEndTimestampMs,
    string? TimelineGameEndEvidenceId) : NarrativeEvidenceV1(Id);

public sealed record NarrativeInterpretationInputV1(
    int Version,
    string InputFingerprint,
    NarrativeUpstreamVersionsV1 UpstreamVersions,
    int ConfiguredParticipantId,
    int? EnemyJunglerParticipantId,
    IReadOnlyList<NarrativeParticipantV1> Participants,
    NarrativeWindowV1 Window,
    NarrativeKnowledgeContextV1 Knowledge,
    IReadOnlyList<NarrativeEvidenceV1> Evidence,
    IReadOnlyList<string> EvidenceLimitations);

public sealed record NarrativeClaimV1(
    string Text,
    string Basis,
    IReadOnlyList<string> EvidenceIds);

public sealed record NarrativeUncertaintyV1(
    string Statement,
    string Reason,
    IReadOnlyList<string> RelatedEvidenceIds);

public sealed record NarrativeThreadV1(
    string Title,
    long StartTimestampMs,
    long EndTimestampMs,
    NarrativeClaimV1 Summary,
    IReadOnlyList<NarrativeClaimV1> SignificantDevelopments);

public sealed record NarrativeInvestigationMomentV1(
    string Title,
    long StartTimestampMs,
    long EndTimestampMs,
    NarrativeClaimV1 WhyItStandsOut,
    string Question,
    IReadOnlyList<NarrativeUncertaintyV1> Uncertainties);

public sealed record NarrativeInterpretationModelOutputV1(
    int Version,
    string InputFingerprint,
    NarrativeClaimV1 Overview,
    IReadOnlyList<NarrativeThreadV1> Threads,
    IReadOnlyList<NarrativeInvestigationMomentV1> MomentsWorthInvestigating,
    IReadOnlyList<NarrativeUncertaintyV1> Uncertainties);

public sealed record NarrativeGenerationMetadataV1(
    string Provider,
    string Model,
    DateTimeOffset GeneratedAtUtc);

public sealed record NarrativeInterpretationV1(
    int Version,
    string InputFingerprint,
    NarrativeUpstreamVersionsV1 UpstreamVersions,
    int PromptPolicyVersion,
    NarrativeGenerationMetadataV1 Generation,
    NarrativeWindowV1 Window,
    NarrativeClaimV1 Overview,
    IReadOnlyList<NarrativeThreadV1> Threads,
    IReadOnlyList<NarrativeInvestigationMomentV1> MomentsWorthInvestigating,
    IReadOnlyList<NarrativeUncertaintyV1> Uncertainties);

public sealed record NarrativeProviderResultV1(
    NarrativeInterpretationModelOutputV1 Output,
    string Provider,
    string Model,
    DateTimeOffset GeneratedAtUtc);

public interface INarrativeInterpretationProvider
{
    Task<NarrativeProviderResultV1> InterpretAsync(
        NarrativeInterpretationInputV1 input,
        CancellationToken cancellationToken);
}

public enum NarrativeInterpretationRequestFailureKind { UnknownWindow, StaleWindow }

public sealed class NarrativeInterpretationRequestException(
    NarrativeInterpretationRequestFailureKind kind,
    string message) : Exception(message)
{
    public NarrativeInterpretationRequestFailureKind Kind { get; } = kind;
}

public enum NarrativeProviderFailureKind { Disabled, InvalidConfiguration, RequestFailed, InvalidResponse }

public sealed class NarrativeProviderException(
    NarrativeProviderFailureKind kind,
    string message,
    int? httpStatus = null) : Exception(message)
{
    public NarrativeProviderFailureKind Kind { get; } = kind;
    public int? HttpStatus { get; } = httpStatus;
}

public sealed class NarrativeOutputValidationException(string message) : Exception(message);
