using Pathwise.Domain.FactualObservations;
using Pathwise.Domain.Reconstruction;
using Pathwise.Domain.ReviewWindows;

namespace Pathwise.Domain.Knowledge;

public sealed class KnowledgeAnnotationGenerator
{
    public const int CurrentVersion = 1;
    public const long InitialSpawnContextRadiusMs = 60_000;

    private static readonly HashSet<string> ElementalDragonSubtypes = new(StringComparer.Ordinal)
    {
        "AIR_DRAGON",
        "EARTH_DRAGON",
        "FIRE_DRAGON",
        "WATER_DRAGON",
        "HEXTECH_DRAGON",
        "CHEMTECH_DRAGON"
    };

    public KnowledgeAnnotationResult Generate(
        GameReconstruction reconstruction,
        FactualObservationResult observations,
        PatchResolution patch,
        KnowledgePack? pack)
    {
        ArgumentNullException.ThrowIfNull(reconstruction);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(patch);
        ValidateInputs(reconstruction, observations, patch, pack);

        if (patch.Patch is null)
            return Result(patch, KnowledgeCoverage.UnknownPatch, null, []);
        if (pack is null)
            return Result(patch, KnowledgeCoverage.NoPackForPatch, null, []);
        if (pack.MapId != reconstruction.MapId || pack.QueueId != reconstruction.QueueId)
            return Result(patch, KnowledgeCoverage.UnsupportedMatch, pack, []);

        var annotations = new Dictionary<AnnotationIdentity, KnowledgeAnnotation>();
        foreach (var window in observations.Windows)
        {
            foreach (var fact in pack.Facts)
                AddAnnotations(window, fact, annotations);
        }

        var ordered = annotations.Values
            .OrderBy(annotation => annotation.Target.Window.RequestedStartTimestampMs)
            .ThenBy(annotation => annotation.Target.Window.RequestedEndTimestampMs)
            .ThenBy(annotation => annotation.Kind)
            .ThenBy(annotation => annotation.FactId, StringComparer.Ordinal)
            .ThenBy(annotation => annotation.RecordedKillTimestampMs)
            .ThenBy(annotation => annotation.Target.Event?.FrameIndex)
            .ThenBy(annotation => annotation.Target.Event?.EventIndex)
            .ToArray();
        return Result(patch, KnowledgeCoverage.Available, pack, ordered);
    }

    private static void AddAnnotations(
        WindowFactualObservations window,
        ObjectiveInitialSpawnFact fact,
        Dictionary<AnnotationIdentity, KnowledgeAnnotation> annotations)
    {
        var matches = window.Observations
            .OfType<EliteObjectiveContextObservation>()
            .SelectMany(observation => observation.Events
                .Where(@event => Matches(fact.Objective, @event))
                .Select(@event => (Observation: observation, Event: @event)))
            .ToArray();

        foreach (var match in matches.Where(match => match.Event.TimestampMs >= fact.InitialSpawnTimestampMs))
        {
            Add(annotations, new(
                fact.Id,
                new(window.Window, match.Observation.Key, match.Event.Source),
                KnowledgeAnnotationKind.RecordedObjectiveContext,
                match.Event.TimestampMs));
        }

        if (matches.Length == 0 && IntersectsInitialSpawnContext(window.Window, fact.InitialSpawnTimestampMs))
        {
            Add(annotations, new(
                fact.Id,
                new(window.Window, null, null),
                KnowledgeAnnotationKind.NearInitialSpawn,
                null));
        }
    }

    private static bool IntersectsInitialSpawnContext(SelectedReviewWindowKey window, long spawnTimestampMs)
    {
        var lower = spawnTimestampMs - InitialSpawnContextRadiusMs;
        var upper = spawnTimestampMs + InitialSpawnContextRadiusMs;
        return window.RequestedEndTimestampMs >= lower && window.RequestedStartTimestampMs < upper;
    }

    private static bool Matches(KnowledgeObjective objective, EliteMonsterKillEvent @event) => objective switch
    {
        KnowledgeObjective.BaronNashor => string.Equals(@event.MonsterType, "BARON_NASHOR", StringComparison.Ordinal),
        KnowledgeObjective.ElementalDragon => string.Equals(@event.MonsterType, "DRAGON", StringComparison.Ordinal) &&
            @event.MonsterSubType is not null && ElementalDragonSubtypes.Contains(@event.MonsterSubType),
        _ => false
    };

    private static void ValidateInputs(
        GameReconstruction reconstruction,
        FactualObservationResult observations,
        PatchResolution patch,
        KnowledgePack? pack)
    {
        if (!string.Equals(patch.RawGameVersion, reconstruction.Patch, StringComparison.Ordinal))
            throw new ArgumentException("Patch resolution does not belong to the reconstruction.", nameof(patch));
        if (observations.GeneratorVersion != FactualObservationGenerator.CurrentVersion)
            throw new ArgumentException("Knowledge Annotation V1 requires Factual Observation V1 output.", nameof(observations));

        foreach (var window in observations.Windows)
        {
            if (window.Window.MatchId != reconstruction.MatchId ||
                window.Window.ConfiguredParticipantId != reconstruction.ConfiguredParticipantId ||
                window.Window.EnemyParticipantId != reconstruction.EnemyResolution.ParticipantId ||
                window.Window.ReconstructionVersion != reconstruction.ReconstructionVersion ||
                window.Window.DetectorVersion != ReviewWindowDetector.CurrentVersion ||
                window.Observations.Any(observation => observation.Key.Window != window.Window))
            {
                throw new ArgumentException("Factual observation identities do not agree with the reconstruction.", nameof(observations));
            }

            foreach (var objective in window.Observations.OfType<EliteObjectiveContextObservation>())
            {
                if (objective.Events.Any(@event =>
                    @event.TimestampMs <= window.Window.RequestedStartTimestampMs ||
                    @event.TimestampMs > window.Window.RequestedEndTimestampMs ||
                    !reconstruction.Events.OfType<EliteMonsterKillEvent>().Any(source =>
                        source.Source == @event.Source && source.TimestampMs == @event.TimestampMs)))
                {
                    throw new ArgumentException("A knowledge target event does not belong to its observation window and reconstruction.", nameof(observations));
                }
            }
        }

        if (pack is null)
            return;
        if (patch.Patch != pack.Patch)
            throw new ArgumentException("Knowledge pack must exactly match the resolved public patch.", nameof(pack));
        if (string.IsNullOrWhiteSpace(pack.Id) || pack.Version <= 0 || string.IsNullOrWhiteSpace(pack.SourceReviewNote))
            throw new ArgumentException("Knowledge pack identity and provenance are required.", nameof(pack));
        if (pack.Facts.Select(fact => fact.Id).Distinct(StringComparer.Ordinal).Count() != pack.Facts.Count)
            throw new ArgumentException("Knowledge fact IDs must be unique.", nameof(pack));
        if (pack.Facts.Any(fact => string.IsNullOrWhiteSpace(fact.Id) ||
            !Enum.IsDefined(fact.Objective) || fact.InitialSpawnTimestampMs < 0 || fact.Sources.Count == 0 ||
            fact.Sources.Any(source => string.IsNullOrWhiteSpace(source.Title) || !source.Url.IsAbsoluteUri)))
        {
            throw new ArgumentException("Knowledge pack contains an invalid objective fact.", nameof(pack));
        }
    }

    private static KnowledgeAnnotationResult Result(
        PatchResolution patch,
        KnowledgeCoverage coverage,
        KnowledgePack? pack,
        IReadOnlyList<KnowledgeAnnotation> annotations) =>
        new(CurrentVersion, patch, coverage, pack, InitialSpawnContextRadiusMs, annotations);

    private static void Add(
        Dictionary<AnnotationIdentity, KnowledgeAnnotation> annotations,
        KnowledgeAnnotation annotation) =>
        annotations.TryAdd(new(annotation.FactId, annotation.Target, annotation.Kind), annotation);

    private sealed record AnnotationIdentity(
        string FactId,
        KnowledgeTarget Target,
        KnowledgeAnnotationKind Kind);
}
