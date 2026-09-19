using Pathwise.Domain.Reconstruction;

namespace Pathwise.Domain.ReviewWindows;

public enum ReviewSignalKind
{
    GoldDifferenceChange,
    XpDifferenceChange,
    ConfiguredPlayerDeath,
    ConcentratedPlayerCombat,
    EliteMonsterKill
}

public enum ReviewSignalDirection { Increase, Decrease, Unchanged, NotApplicable }

public enum ReviewSelectionReason
{
    GoldAndXpChange,
    GoldChange,
    XpChange,
    ConfiguredPlayerDeath,
    ConcentratedPlayerCombat,
    EliteMonsterKill
}

public enum SkippedComparisonReason { AdjacentFrameGap, ConfiguredPlayerCounterRegression, EnemyCounterRegression }
public enum SuppressionReason { DuplicateOverlap, TriggerAlreadyAssigned, CandidateCap }

public sealed record ReviewWindowOptions(
    long MetricLookbackMs,
    long GoldChangeThreshold,
    long XpChangeThreshold,
    long CombatLookbackMs,
    int CombatMinimumDistinctEvents,
    long EventContextPaddingMs,
    long MaximumMergedDurationMs,
    double DuplicateOverlapFraction,
    int MaximumSelectedWindows,
    long MaximumAdjacentFrameGapMs)
{
    public static ReviewWindowOptions Default { get; } = new(
        MetricLookbackMs: 180_000,
        GoldChangeThreshold: 1_000,
        XpChangeThreshold: 1_500,
        CombatLookbackMs: 60_000,
        CombatMinimumDistinctEvents: 3,
        EventContextPaddingMs: 60_000,
        MaximumMergedDurationMs: 300_000,
        DuplicateOverlapFraction: 0.5,
        MaximumSelectedWindows: 5,
        MaximumAdjacentFrameGapMs: 90_000);
}

public sealed record SourceFrameReference(int FrameIndex, long TimestampMs);

public sealed record ReviewSignal(
    ReviewSignalKind Kind,
    long StartTimestampMs,
    long EndTimestampMs,
    IReadOnlyList<SourceFrameReference> SourceFrames,
    IReadOnlyList<SourceEventReference> SourceEvents,
    long? StartValue,
    long? EndValue,
    long? SignedChange,
    long? Threshold,
    ReviewSignalDirection Direction,
    int DistinctEventCount);

public sealed record RelativePlayerValues(
    long TotalGold,
    long Xp,
    int Level,
    long JungleCs,
    long LaneCs,
    int Kills,
    int Deaths,
    int Assists);

public sealed record RelativeWindowEvidence(
    RelativePlayerValues Start,
    RelativePlayerValues End,
    RelativePlayerValues Change);

public sealed record SkippedMetricComparison(
    ReviewSignalKind Metric,
    SourceFrameReference StartFrame,
    SourceFrameReference EndFrame,
    SkippedComparisonReason Reason,
    int ParticipantId,
    long? PreviousValue,
    long? CurrentValue);

public sealed record SuppressedReviewSeed(
    int SelectedWindowRank,
    SuppressionReason Reason,
    long StartTimestampMs,
    long EndTimestampMs,
    IReadOnlyList<ReviewSignal> Signals);

public sealed record ReviewWindowCandidate(
    int SelectionRank,
    ReviewSelectionReason PrimarySelectionReason,
    long RequestedStartTimestampMs,
    long RequestedEndTimestampMs,
    Participant ConfiguredParticipant,
    Participant? EnemyJungler,
    EnemyJunglerResolution EnemyResolution,
    GameChanges Changes,
    RelativeWindowEvidence? RelativeEvidence,
    IReadOnlyList<ReviewSignal> Signals,
    IReadOnlyList<ReviewSignal> AbsorbedSignals,
    IReadOnlyList<SourceEventReference> TriggerEventReferences,
    IReadOnlyList<ReconstructionEvent> SupportingEvents,
    IReadOnlyList<SourceDataIssue> ReconstructionIssues,
    IReadOnlyList<SuppressedReviewSeed> SuppressedSeeds);

public sealed record ReviewWindowDetectionResult(
    IReadOnlyList<ReviewWindowCandidate> Candidates,
    int DetectorVersion,
    int ReconstructionVersion,
    ReviewWindowOptions EffectiveOptions,
    EnemyJunglerResolution EnemyResolution,
    IReadOnlyList<SourceDataIssue> SourceIssues,
    IReadOnlyList<SkippedMetricComparison> SkippedComparisons,
    IReadOnlyList<SuppressedReviewSeed> SuppressedSeeds,
    IReadOnlyList<SuppressedReviewSeed> UnselectedDueToCap);
