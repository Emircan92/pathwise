namespace Pathwise.Api;

public sealed record NarrativeInterpretationRequest(
    long RequestedStartTimestampMs,
    long RequestedEndTimestampMs,
    int ReconstructionVersion,
    int DetectorVersion);
