using Pathwise.Domain.FactualObservations;
using Pathwise.Domain.Reconstruction;

namespace Pathwise.Domain.Knowledge;

public sealed record PublicPatch(int Major, int Minor);

public sealed record PatchResolution(
    string RawGameVersion,
    PublicPatch? Patch,
    string Basis);

public sealed record KnowledgeSource(
    string Title,
    Uri Url);

public enum KnowledgeObjective
{
    ElementalDragon,
    BaronNashor
}

public sealed record ObjectiveInitialSpawnFact
{
    public ObjectiveInitialSpawnFact(
        string id,
        KnowledgeObjective objective,
        long initialSpawnTimestampMs,
        IReadOnlyList<KnowledgeSource> sources)
    {
        Id = id;
        Objective = objective;
        InitialSpawnTimestampMs = initialSpawnTimestampMs;
        Sources = Array.AsReadOnly(sources.ToArray());
    }

    public string Id { get; }
    public KnowledgeObjective Objective { get; }
    public long InitialSpawnTimestampMs { get; }
    public IReadOnlyList<KnowledgeSource> Sources { get; }
}

public sealed record KnowledgePack
{
    public KnowledgePack(
        string id,
        int version,
        PublicPatch patch,
        int mapId,
        int queueId,
        string sourceReviewNote,
        IReadOnlyList<ObjectiveInitialSpawnFact> facts)
    {
        Id = id;
        Version = version;
        Patch = patch;
        MapId = mapId;
        QueueId = queueId;
        SourceReviewNote = sourceReviewNote;
        Facts = Array.AsReadOnly(facts.ToArray());
    }

    public string Id { get; }
    public int Version { get; }
    public PublicPatch Patch { get; }
    public int MapId { get; }
    public int QueueId { get; }
    public string SourceReviewNote { get; }
    public IReadOnlyList<ObjectiveInitialSpawnFact> Facts { get; }
}

public enum KnowledgeCoverage
{
    Available,
    UnknownPatch,
    NoPackForPatch,
    UnsupportedMatch
}

public enum KnowledgeAnnotationKind
{
    NearInitialSpawn,
    RecordedObjectiveContext
}

public sealed record KnowledgeTarget(
    SelectedReviewWindowKey Window,
    FactualObservationKey? Observation,
    SourceEventReference? Event);

public sealed record KnowledgeAnnotation(
    string FactId,
    KnowledgeTarget Target,
    KnowledgeAnnotationKind Kind,
    long? RecordedKillTimestampMs);

public sealed record KnowledgeAnnotationResult
{
    public KnowledgeAnnotationResult(
        int generatorVersion,
        PatchResolution patchResolution,
        KnowledgeCoverage coverage,
        KnowledgePack? pack,
        long initialSpawnContextRadiusMs,
        IReadOnlyList<KnowledgeAnnotation> annotations)
    {
        GeneratorVersion = generatorVersion;
        PatchResolution = patchResolution;
        Coverage = coverage;
        Pack = pack;
        InitialSpawnContextRadiusMs = initialSpawnContextRadiusMs;
        Annotations = Array.AsReadOnly(annotations.ToArray());
    }

    public int GeneratorVersion { get; }
    public PatchResolution PatchResolution { get; }
    public KnowledgeCoverage Coverage { get; }
    public KnowledgePack? Pack { get; }
    public long InitialSpawnContextRadiusMs { get; }
    public IReadOnlyList<KnowledgeAnnotation> Annotations { get; }
}
