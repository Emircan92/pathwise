using Pathwise.Application.Ingestion;
using Pathwise.Application.Knowledge;
using Pathwise.Application.Reconstruction;
using Pathwise.Domain.FactualObservations;
using Pathwise.Domain.Knowledge;
using Pathwise.Domain.ReviewWindows;

namespace Pathwise.Application.ReviewWindows;

public sealed record MatchReview(
    ReviewWindowDetectionResult WindowDetection,
    FactualObservationResult FactualObservations,
    KnowledgeAnnotationResult KnowledgeAnnotations)
{
    public IReadOnlyDictionary<SelectedReviewWindowKey, IReadOnlyList<ReviewPositionSample>> PositionSamplesByWindow { get; init; }
        = new Dictionary<SelectedReviewWindowKey, IReadOnlyList<ReviewPositionSample>>();
}

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
        var patch = new PublicPatchResolver().Resolve(reviewWindows.Reconstruction.Patch);
        var knowledge = new KnowledgeAnnotationGenerator().Generate(
            reviewWindows.Reconstruction,
            observations,
            patch,
            KnowledgePacks.For(patch.Patch));
        return new(
            reviewWindows.Reconstruction,
            reviewWindows.Source,
            new MatchReview(reviewWindows.Value, observations, knowledge)
            {
                PositionSamplesByWindow = reviewWindows.Value.Candidates.ToDictionary(
                    candidate => ReviewPositionSelector.KeyFor(reviewWindows.Reconstruction, reviewWindows.Value, candidate),
                    candidate => ReviewPositionSelector.Select(reviewWindows.Reconstruction, candidate))
            });
    }
}
