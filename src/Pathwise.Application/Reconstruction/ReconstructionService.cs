using Pathwise.Application.Ingestion;
using Pathwise.Domain.Reconstruction;

namespace Pathwise.Application.Reconstruction;

public sealed class ReconstructionService(
    IMatchStore matchStore,
    IStoredReconstructionSource source)
{
    public async Task<ReconstructionResult<GameReconstruction>> GetMetadataAsync(
        RiotSettingsSnapshot settings,
        string matchId,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(settings, matchId, cancellationToken);
        return new(loaded.Reconstruction, loaded.Metadata, loaded.Reconstruction);
    }

    public async Task<ReconstructionResult<GameState>> GetStateAsync(
        RiotSettingsSnapshot settings,
        string matchId,
        long atMs,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(settings, matchId, cancellationToken);
        return new(loaded.Reconstruction, loaded.Metadata, loaded.Reconstruction.StateAt(atMs));
    }

    public async Task<ReconstructionResult<GameChanges>> GetChangesAsync(
        RiotSettingsSnapshot settings,
        string matchId,
        long fromMs,
        long toMs,
        CancellationToken cancellationToken)
    {
        // Both endpoint states intentionally come from this single reconstruction instance.
        var loaded = await LoadAsync(settings, matchId, cancellationToken);
        return new(loaded.Reconstruction, loaded.Metadata, loaded.Reconstruction.Changes(fromMs, toMs));
    }

    private async Task<LoadedReconstruction> LoadAsync(
        RiotSettingsSnapshot settings,
        string matchId,
        CancellationToken cancellationToken)
    {
        var player = await matchStore.GetResolvedPlayerAsync(settings, cancellationToken);
        if (player?.Puuid is null)
            throw new ReconstructionRequestException(new(
                ReconstructionFailureKind.MatchUnavailable,
                "configured_account_unavailable",
                "The configured account has not been resolved locally."));

        var result = await source.LoadAsync(matchId, player.Puuid, cancellationToken);
        if (result.Failure is { } failure)
            throw new ReconstructionRequestException(failure);

        var stored = result.Source!;
        return new(new GameReconstruction(stored.Input), stored.Metadata);
    }

    private sealed record LoadedReconstruction(
        GameReconstruction Reconstruction,
        ReconstructionSourceMetadata Metadata);
}
