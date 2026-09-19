using Pathwise.Application.Ingestion;
using Pathwise.Application.Reconstruction;
using Pathwise.Domain.ReviewWindows;

namespace Pathwise.Application.ReviewWindows;

public sealed class ReviewWindowService(ReconstructionService reconstructionService)
{
    public async Task<ReconstructionResult<ReviewWindowDetectionResult>> GetAsync(
        RiotSettingsSnapshot settings,
        string matchId,
        CancellationToken cancellationToken)
    {
        var reconstruction = await reconstructionService.GetMetadataAsync(settings, matchId, cancellationToken);
        var result = new ReviewWindowDetector().Detect(reconstruction.Reconstruction, ReviewWindowOptions.Default);
        return new(reconstruction.Reconstruction, reconstruction.Source, result);
    }
}
