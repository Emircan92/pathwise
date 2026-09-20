using Pathwise.Application.Ingestion;
using Pathwise.Application.Reconstruction;
using Pathwise.Domain.FactualObservations;
using Pathwise.Domain.ReviewWindows;

namespace Pathwise.Application.ReviewWindows;

public sealed record MatchReview(
    ReviewWindowDetectionResult WindowDetection,
    FactualObservationResult FactualObservations);

public sealed class MatchReviewService(ReviewWindowService reviewWindowService)
{
    public async Task<ReconstructionResult<MatchReview>> GetAsync(
        RiotSettingsSnapshot settings,
        string matchId,
        CancellationToken cancellationToken)
    {
        var reviewWindows = await reviewWindowService.GetAsync(settings, matchId, cancellationToken);
        var observations = new FactualObservationGenerator().Generate(
            reviewWindows.Reconstruction,
            reviewWindows.Value);
        return new(
            reviewWindows.Reconstruction,
            reviewWindows.Source,
            new(reviewWindows.Value, observations));
    }
}
