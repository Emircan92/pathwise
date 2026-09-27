using Pathwise.Domain.Reconstruction;
using Pathwise.Domain.ReviewWindows;

namespace Pathwise.Domain.Progression;

public abstract record ProgressionEvent(
    long TimestampMs,
    SourceEventReference Source);

public sealed record BuildingDestroyedProgressionEvent(
    long TimestampMs,
    SourceEventReference Source,
    string? BuildingType,
    string? TowerType,
    string? LaneType,
    TeamAttribution StructureOwnerTeam,
    int? KillerParticipantId,
    IReadOnlyList<int> AssistingParticipantIds,
    Position? Position)
    : ProgressionEvent(TimestampMs, Source);

public sealed record RiftHeraldKilledProgressionEvent(
    long TimestampMs,
    SourceEventReference Source,
    int? KillerParticipantId,
    TeamAttribution TeamAttribution,
    IReadOnlyList<int> AssistingParticipantIds,
    Position? Position)
    : ProgressionEvent(TimestampMs, Source);

public sealed record ItemDestroyedProgressionEvent(
    long TimestampMs,
    SourceEventReference Source,
    int? ParticipantId,
    int ItemId)
    : ProgressionEvent(TimestampMs, Source);

public sealed record GameEndedProgressionEvent(
    long TimestampMs,
    SourceEventReference Source,
    TeamAttribution WinningTeam)
    : ProgressionEvent(TimestampMs, Source);

public sealed record ProgressionOutcomeEvidence(
    int ConfiguredParticipantId,
    int ConfiguredTeamId,
    MatchFieldFact<bool>? ConfiguredPlayerWon,
    int? ResolvedWinningTeamId,
    MatchFieldFact<int> ReportedDurationSeconds,
    MatchFieldFact<long>? MatchEndTimestampMs,
    MatchFieldFact<string>? EndOfGameResult,
    IReadOnlyList<MatchTeamResultEvidence> TeamResults,
    IReadOnlyList<MatchParticipantResultEvidence> ParticipantResults,
    GameEndedProgressionEvent? TimelineGameEnd);

public sealed record WindowProgressionEvidence(
    ReviewWindowCandidate Window,
    bool ContainsGameEnd,
    IReadOnlyList<ProgressionEvent> Events);

public sealed record ProgressionEvidenceResult(
    int ProjectorVersion,
    int ReconstructionVersion,
    ProgressionOutcomeEvidence Outcome,
    IReadOnlyList<ProgressionEvent> Events,
    IReadOnlyList<WindowProgressionEvidence> Windows);
