using Pathwise.Domain.Reconstruction;

namespace Pathwise.Application.Reconstruction;

public enum ReconstructionFailureKind
{
    MatchUnavailable,
    PayloadNotReady,
    Unsupported,
    InvalidSource
}

public sealed record ReconstructionFailure(ReconstructionFailureKind Kind, string Code, string Message);

public sealed record ReconstructionSourceMetadata(
    DateTimeOffset MatchRetrievedAtUtc,
    DateTimeOffset TimelineRetrievedAtUtc,
    string MatchEndpointVersion,
    string TimelineEndpointVersion);

public sealed record StoredReconstructionSource(
    ReconstructionInput Input,
    ReconstructionSourceMetadata Metadata);

public sealed record StoredReconstructionLoadResult(
    StoredReconstructionSource? Source,
    ReconstructionFailure? Failure)
{
    public static StoredReconstructionLoadResult Success(StoredReconstructionSource source) => new(source, null);
    public static StoredReconstructionLoadResult Failed(ReconstructionFailure failure) => new(null, failure);
}

public interface IStoredReconstructionSource
{
    Task<StoredReconstructionLoadResult> LoadAsync(
        string matchId,
        string configuredPlayerPuuid,
        CancellationToken cancellationToken);
}

public sealed record ReconstructionResult<T>(
    GameReconstruction Reconstruction,
    ReconstructionSourceMetadata Source,
    T Value);

public sealed class ReconstructionRequestException(ReconstructionFailure failure) : Exception(failure.Message)
{
    public ReconstructionFailure Failure { get; } = failure;
}
