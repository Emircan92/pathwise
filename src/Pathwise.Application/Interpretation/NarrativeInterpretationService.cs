using Pathwise.Application.Ingestion;
using Pathwise.Application.ReviewWindows;

namespace Pathwise.Application.Interpretation;

public sealed class NarrativeInterpretationService(
    MatchReviewService matchReviewService,
    INarrativeInterpretationProvider provider)
{
    public async Task<NarrativeInterpretationV1> InterpretAsync(
        RiotSettingsSnapshot settings,
        string matchId,
        NarrativeInterpretationSelection selection,
        CancellationToken cancellationToken)
    {
        var review = await matchReviewService.GetAsync(settings, matchId, cancellationToken);
        if (review.Reconstruction.ReconstructionVersion != selection.ReconstructionVersion ||
            review.Value.WindowDetection.DetectorVersion != selection.DetectorVersion)
        {
            throw new NarrativeInterpretationRequestException(
                NarrativeInterpretationRequestFailureKind.StaleWindow,
                "The selected review period was produced by an older analysis version. Reload the match review and try again.");
        }

        var candidate = review.Value.WindowDetection.Candidates.SingleOrDefault(value =>
            value.RequestedStartTimestampMs == selection.RequestedStartTimestampMs &&
            value.RequestedEndTimestampMs == selection.RequestedEndTimestampMs);
        if (candidate is null)
        {
            throw new NarrativeInterpretationRequestException(
                NarrativeInterpretationRequestFailureKind.UnknownWindow,
                "The requested period is not a final selected review period for this match.");
        }

        var input = NarrativeInterpretationProjectorV1.Project(review, candidate);
        var output = await provider.InterpretAsync(input, cancellationToken);
        return NarrativeInterpretationValidatorV1.Validate(input, output);
    }
}
