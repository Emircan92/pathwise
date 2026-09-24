using Pathwise.Domain.FactualObservations;
using Pathwise.Domain.Reconstruction;
using Pathwise.Domain.ReviewWindows;

namespace Pathwise.Application.ReviewWindows;

public sealed record ReviewPositionSample(
    int FrameIndex,
    long TimestampMs,
    Position? ConfiguredPlayerPosition,
    Position? EnemyJunglerPosition);

public static class ReviewPositionSelector
{
    public static SelectedReviewWindowKey KeyFor(
        GameReconstruction reconstruction,
        ReviewWindowDetectionResult detection,
        ReviewWindowCandidate candidate) => new(
            reconstruction.MatchId,
            reconstruction.ConfiguredParticipantId,
            reconstruction.EnemyResolution.ParticipantId,
            candidate.RequestedStartTimestampMs,
            candidate.RequestedEndTimestampMs,
            reconstruction.ReconstructionVersion,
            detection.DetectorVersion);

    public static IReadOnlyList<ReviewPositionSample> Select(GameReconstruction reconstruction, ReviewWindowCandidate candidate)
    {
        var startFrameIndex = candidate.Changes.StartState.SelectedFrameIndex;
        return reconstruction.Observations
            .Where(frame => frame.FrameIndex == startFrameIndex ||
                (frame.TimestampMs > candidate.RequestedStartTimestampMs && frame.TimestampMs <= candidate.RequestedEndTimestampMs))
            .DistinctBy(frame => frame.FrameIndex)
            .OrderBy(frame => frame.TimestampMs)
            .ThenBy(frame => frame.FrameIndex)
            .Select(frame => new ReviewPositionSample(
                frame.FrameIndex,
                frame.TimestampMs,
                frame.ConfiguredPlayer.Position,
                reconstruction.EnemyResolution.Status == EnemyResolutionStatus.Resolved ? frame.EnemyJungler?.Position : null))
            .ToArray();
    }
}
