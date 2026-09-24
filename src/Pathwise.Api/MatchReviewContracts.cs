using System.Text.Json.Serialization;
using Pathwise.Application.Reconstruction;
using Pathwise.Application.ReviewWindows;
using Pathwise.Domain.FactualObservations;
using Pathwise.Domain.Knowledge;
using Pathwise.Domain.Reconstruction;
using Pathwise.Domain.ReviewWindows;

namespace Pathwise.Api;

public sealed record MatchReviewResponse(
    string MatchId,
    int MapId,
    int ConfiguredParticipantId,
    IReadOnlyList<ReviewParticipantDto> Participants,
    EnemyResolutionDto EnemyResolution,
    MatchReviewVersionsDto Versions,
    MatchReviewKnowledgeDto Knowledge,
    IReadOnlyList<SourceDataIssueDto> SourceDataIssues,
    IReadOnlyList<ReviewWindowDto> Windows);

public sealed record MatchReviewVersionsDto(
    int Reconstruction,
    int Detector,
    int FactualObservations,
    int KnowledgeAnnotations);

public sealed record MatchReviewKnowledgeDto(string? PublicPatch, string Coverage);
public sealed record SourceFrameDto(int FrameIndex, long TimestampMs);
public sealed record ReviewParticipantDto(int ParticipantId, string ChampionName, int TeamId);
public sealed record ReviewPositionSampleDto(int FrameIndex, long TimestampMs, PositionDto? ConfiguredPlayerPosition, PositionDto? EnemyJunglerPosition);

public sealed record ReviewWindowDto(
    long RequestedStartTimestampMs,
    long RequestedEndTimestampMs,
    SourceFrameDto StartFrame,
    SourceFrameDto EndFrame,
    int SelectionRank,
    string PrimarySelectionReason,
    IReadOnlyList<string> SignalKinds,
    IReadOnlyList<string> AbsorbedSignalKinds,
    IReadOnlyList<ObservationDto> Observations,
    IReadOnlyList<MetricOmissionDto> MetricOmissions,
    IReadOnlyList<KnowledgeAnnotationDto> KnowledgeAnnotations,
    IReadOnlyList<ReviewPositionSampleDto> PositionSamples);

public sealed record MetricOmissionDto(string Kind, string Reason);
public sealed record MetricEndpointDto(long StartValue, long EndValue);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(RelativeGoldMovementDto), "relativeGoldMovement")]
[JsonDerivedType(typeof(RelativeXpMovementDto), "relativeXpMovement")]
[JsonDerivedType(typeof(RelativeJungleCsMovementDto), "relativeJungleCsMovement")]
[JsonDerivedType(typeof(ConfiguredPlayerCombatDto), "configuredPlayerCombat")]
[JsonDerivedType(typeof(EliteObjectiveContextDto), "eliteObjectiveContext")]
public abstract record ObservationDto;

public abstract record MetricObservationDto(
    long RelativeStart,
    long RelativeEnd,
    long SignedChange,
    MetricEndpointDto ConfiguredPlayer,
    MetricEndpointDto EnemyJungler) : ObservationDto;

public sealed record RelativeGoldMovementDto(
    long RelativeStart,
    long RelativeEnd,
    long SignedChange,
    MetricEndpointDto ConfiguredPlayer,
    MetricEndpointDto EnemyJungler)
    : MetricObservationDto(RelativeStart, RelativeEnd, SignedChange, ConfiguredPlayer, EnemyJungler);

public sealed record RelativeXpMovementDto(
    long RelativeStart,
    long RelativeEnd,
    long SignedChange,
    MetricEndpointDto ConfiguredPlayer,
    MetricEndpointDto EnemyJungler)
    : MetricObservationDto(RelativeStart, RelativeEnd, SignedChange, ConfiguredPlayer, EnemyJungler);

public sealed record RelativeJungleCsMovementDto(
    long RelativeStart,
    long RelativeEnd,
    long SignedChange,
    MetricEndpointDto ConfiguredPlayer,
    MetricEndpointDto EnemyJungler)
    : MetricObservationDto(RelativeStart, RelativeEnd, SignedChange, ConfiguredPlayer, EnemyJungler);

public sealed record ConfiguredPlayerCombatDto(
    int Kills,
    int Deaths,
    int Assists,
    int DistinctEventCount,
    IReadOnlyList<CombatEventDto> Events) : ObservationDto;

public sealed record EliteObjectiveContextDto(IReadOnlyList<ObjectiveEventDto> Events) : ObservationDto;

public sealed record CombatEventDto(
    SourceEventReferenceDto Source,
    long TimestampMs,
    int? KillerParticipantId,
    int VictimParticipantId,
    IReadOnlyList<int> AssistingParticipantIds,
    PositionDto? Position);

public sealed record ObjectiveEventDto(
    SourceEventReferenceDto Source,
    long TimestampMs,
    string? MonsterType,
    string? MonsterSubType,
    int? KillerParticipantId,
    IReadOnlyList<int> AssistingParticipantIds,
    TeamAttributionDto TeamAttribution,
    PositionDto? Position);

public sealed record KnowledgeSourceDto(string Title, string Url);
public sealed record KnowledgeFactDto(
    string Id,
    string Objective,
    long InitialSpawnTimestampMs,
    IReadOnlyList<KnowledgeSourceDto> Sources);
public sealed record KnowledgeTargetDto(string? ObservationKind, SourceEventReferenceDto? Event);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(NearInitialSpawnDto), "nearInitialSpawn")]
[JsonDerivedType(typeof(RecordedObjectiveContextDto), "recordedObjectiveContext")]
public abstract record KnowledgeAnnotationDto(
    KnowledgeFactDto Fact,
    KnowledgeTargetDto Target,
    long? RecordedKillTimestampMs);

public sealed record NearInitialSpawnDto(
    KnowledgeFactDto Fact,
    KnowledgeTargetDto Target,
    long? RecordedKillTimestampMs)
    : KnowledgeAnnotationDto(Fact, Target, RecordedKillTimestampMs);

public sealed record RecordedObjectiveContextDto(
    KnowledgeFactDto Fact,
    KnowledgeTargetDto Target,
    long? RecordedKillTimestampMs)
    : KnowledgeAnnotationDto(Fact, Target, RecordedKillTimestampMs);

public static class MatchReviewApiMapper
{
    public static MatchReviewResponse Map(ReconstructionResult<MatchReview> result)
    {
        var review = result.Value;
        var observationsByWindow = review.FactualObservations.Windows.ToDictionary(window => window.Window);
        var annotationsByWindow = review.KnowledgeAnnotations.Annotations
            .GroupBy(annotation => annotation.Target.Window)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<KnowledgeAnnotation>)group.ToArray());
        var factsById = review.KnowledgeAnnotations.Pack?.Facts.ToDictionary(fact => fact.Id, StringComparer.Ordinal)
            ?? new Dictionary<string, ObjectiveInitialSpawnFact>(StringComparer.Ordinal);

        var windows = review.WindowDetection.Candidates.Select(candidate =>
        {
            var key = ReviewPositionSelector.KeyFor(result.Reconstruction, review.WindowDetection, candidate);
            if (!observationsByWindow.TryGetValue(key, out var factual))
                throw new InvalidOperationException("A selected review window has no matching factual-observation result.");

            annotationsByWindow.TryGetValue(key, out var annotations);
            if (!review.PositionSamplesByWindow.TryGetValue(key, out var positionSamples))
                throw new InvalidOperationException("A selected review window has no matching position samples.");
            return MapWindow(candidate, factual, annotations ?? [], factsById, positionSamples);
        }).ToArray();

        var patch = review.KnowledgeAnnotations.PatchResolution.Patch;
        return new(
            result.Reconstruction.MatchId,
            result.Reconstruction.MapId,
            result.Reconstruction.ConfiguredParticipantId,
            result.Reconstruction.Participants.OrderBy(participant => participant.ParticipantId)
                .Select(participant => new ReviewParticipantDto(participant.ParticipantId, participant.ChampionName, participant.TeamId)).ToArray(),
            Enemy(result.Reconstruction.EnemyResolution),
            new(
                result.Reconstruction.ReconstructionVersion,
                review.WindowDetection.DetectorVersion,
                review.FactualObservations.GeneratorVersion,
                review.KnowledgeAnnotations.GeneratorVersion),
            new(patch is null ? null : $"{patch.Major}.{patch.Minor}", Camel(review.KnowledgeAnnotations.Coverage)),
            review.WindowDetection.SourceIssues.Select(Issue).ToArray(),
            windows);
    }

    private static ReviewWindowDto MapWindow(
        ReviewWindowCandidate candidate,
        WindowFactualObservations factual,
        IReadOnlyList<KnowledgeAnnotation> annotations,
        IReadOnlyDictionary<string, ObjectiveInitialSpawnFact> factsById,
        IReadOnlyList<ReviewPositionSample> positionSamples) => new(
            candidate.RequestedStartTimestampMs,
            candidate.RequestedEndTimestampMs,
            Frame(candidate.Changes.StartState),
            Frame(candidate.Changes.EndState),
            candidate.SelectionRank,
            Camel(candidate.PrimarySelectionReason),
            SignalKinds(candidate.Signals),
            SignalKinds(candidate.AbsorbedSignals),
            factual.Observations.Select(Observation).ToArray(),
            factual.Omissions.Select(omission => new MetricOmissionDto(Camel(omission.Kind), "counterRegression")).ToArray(),
            annotations.Select(annotation => Annotation(annotation, factsById)).ToArray(),
            positionSamples.Select(sample => new ReviewPositionSampleDto(sample.FrameIndex, sample.TimestampMs,
                Position(sample.ConfiguredPlayerPosition), Position(sample.EnemyJunglerPosition))).ToArray());

    private static ObservationDto Observation(FactualObservation observation) => observation switch
    {
        RelativeGoldObservation value => Metric(value.Evidence, (start, end, change, player, enemy) => new RelativeGoldMovementDto(start, end, change, player, enemy)),
        RelativeXpObservation value => Metric(value.Evidence, (start, end, change, player, enemy) => new RelativeXpMovementDto(start, end, change, player, enemy)),
        RelativeJungleCsObservation value => Metric(value.Evidence, (start, end, change, player, enemy) => new RelativeJungleCsMovementDto(start, end, change, player, enemy)),
        ConfiguredPlayerCombatObservation value => new ConfiguredPlayerCombatDto(
            value.KillCount,
            value.DeathCount,
            value.AssistCount,
            value.DistinctEventCount,
            value.Events.Select(@event => new CombatEventDto(Source(@event.Source), @event.TimestampMs, @event.KillerParticipantId, @event.VictimParticipantId, @event.AssistingParticipantIds, Position(@event.Position))).ToArray()),
        EliteObjectiveContextObservation value => new EliteObjectiveContextDto(
            value.Events.Select(@event => new ObjectiveEventDto(
                Source(@event.Source),
                @event.TimestampMs,
                @event.MonsterType,
                @event.MonsterSubType,
                @event.KillerParticipantId,
                @event.AssistingParticipantIds,
                Team(@event.TeamAttribution),
                Position(@event.Position))).ToArray()),
        _ => throw new InvalidOperationException($"Unsupported factual observation {observation.GetType().Name}.")
    };

    private static ObservationDto Metric(
        RelativeMetricEvidence evidence,
        Func<long, long, long, MetricEndpointDto, MetricEndpointDto, ObservationDto> create) => create(
            evidence.RelativeStart,
            evidence.RelativeEnd,
            evidence.SignedChange,
            new(evidence.ConfiguredPlayer.StartValue, evidence.ConfiguredPlayer.EndValue),
            new(evidence.EnemyJungler.StartValue, evidence.EnemyJungler.EndValue));

    private static KnowledgeAnnotationDto Annotation(
        KnowledgeAnnotation annotation,
        IReadOnlyDictionary<string, ObjectiveInitialSpawnFact> factsById)
    {
        if (!factsById.TryGetValue(annotation.FactId, out var fact))
            throw new InvalidOperationException($"Knowledge annotation references unknown fact '{annotation.FactId}'.");

        var factDto = new KnowledgeFactDto(
            fact.Id,
            Camel(fact.Objective),
            fact.InitialSpawnTimestampMs,
            fact.Sources.Select(source => new KnowledgeSourceDto(source.Title, source.Url.AbsoluteUri)).ToArray());
        var target = new KnowledgeTargetDto(
            annotation.Target.Observation is null ? null : Camel(annotation.Target.Observation.Kind),
            annotation.Target.Event is null ? null : Source(annotation.Target.Event));
        return annotation.Kind switch
        {
            KnowledgeAnnotationKind.NearInitialSpawn => new NearInitialSpawnDto(factDto, target, annotation.RecordedKillTimestampMs),
            KnowledgeAnnotationKind.RecordedObjectiveContext => new RecordedObjectiveContextDto(factDto, target, annotation.RecordedKillTimestampMs),
            _ => throw new InvalidOperationException($"Unsupported knowledge annotation {annotation.Kind}.")
        };
    }

    private static IReadOnlyList<string> SignalKinds(IReadOnlyList<ReviewSignal> signals) => signals
        .Select(signal => signal.Kind)
        .Distinct()
        .OrderBy(kind => kind)
        .Select(Camel)
        .ToArray();

    private static SourceFrameDto Frame(GameState state) => new(state.SelectedFrameIndex, state.SelectedFrameTimestampMs);
    private static SourceDataIssueDto Issue(SourceDataIssue value) => new(value.Code, value.SourceReference, value.Explanation, value.HandlingOutcome);
    private static EnemyResolutionDto Enemy(EnemyJunglerResolution value) => new(Camel(value.Status), value.ParticipantId);
    private static TeamAttributionDto Team(TeamAttribution value) => new(value.Kind.ToString(), value.SuppliedTeamId, value.ResolvedTeamId, value.DiagnosticReason);
    private static SourceEventReferenceDto Source(SourceEventReference value) => new(value.FrameIndex, value.EventIndex);
    private static PositionDto? Position(Position? value) => value is null ? null : new(value.X, value.Y);
    private static string Camel<T>(T value) where T : struct, Enum
    {
        var text = value.ToString();
        return char.ToLowerInvariant(text[0]) + text[1..];
    }
}
