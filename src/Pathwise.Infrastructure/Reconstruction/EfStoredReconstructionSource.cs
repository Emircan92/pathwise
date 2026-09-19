using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pathwise.Application.Ingestion;
using Pathwise.Application.Reconstruction;
using Pathwise.Infrastructure.Persistence;

namespace Pathwise.Infrastructure.Reconstruction;

public sealed class EfStoredReconstructionSource(
    IDbContextFactory<PathwiseDbContext> dbFactory,
    RiotReconstructionMapper mapper,
    ILogger<EfStoredReconstructionSource> logger) : IStoredReconstructionSource
{
    public async Task<StoredReconstructionLoadResult> LoadAsync(
        string matchId,
        string configuredPlayerPuuid,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var match = await db.StoredMatches
            .AsNoTracking()
            .Include(x => x.Payloads)
            .SingleOrDefaultAsync(x => x.MatchId == matchId && x.PlayerPuuid == configuredPlayerPuuid, cancellationToken);

        if (match is null)
            return Failed(matchId, new(ReconstructionFailureKind.MatchUnavailable, "match_unavailable", "The match is unavailable for the configured local account."));

        var matchPayload = match.Payloads.SingleOrDefault(x => x.Kind == PayloadKind.Match);
        var timelinePayload = match.Payloads.SingleOrDefault(x => x.Kind == PayloadKind.Timeline);
        if (matchPayload?.State != PayloadState.Stored || timelinePayload?.State != PayloadState.Stored ||
            matchPayload.RawJson is null || timelinePayload.RawJson is null ||
            matchPayload.RetrievedAtUtc is null || timelinePayload.RetrievedAtUtc is null)
            return Failed(matchId, new(ReconstructionFailureKind.PayloadNotReady, "payload_not_ready", "Required match and timeline payloads are not both stored and ready."));

        try
        {
            var input = mapper.Map(matchPayload.RawJson, timelinePayload.RawJson, matchId, configuredPlayerPuuid);
            return StoredReconstructionLoadResult.Success(new(
                input,
                new(
                    matchPayload.RetrievedAtUtc.Value,
                    timelinePayload.RetrievedAtUtc.Value,
                    matchPayload.EndpointVersion,
                    timelinePayload.EndpointVersion)));
        }
        catch (ReconstructionMappingException ex)
        {
            var kind = ex.Code is "unsupported_queue" or "unsupported_role"
                ? ReconstructionFailureKind.Unsupported
                : ReconstructionFailureKind.InvalidSource;
            return Failed(matchId, new(kind, ex.Code, ex.Message));
        }
    }

    private StoredReconstructionLoadResult Failed(string matchId, ReconstructionFailure failure)
    {
        logger.LogWarning("Reconstruction load failed for match {MatchId} with issue {IssueCode}", matchId, failure.Code);
        return StoredReconstructionLoadResult.Failed(failure);
    }
}
