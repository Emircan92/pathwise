using Pathwise.Domain.Reconstruction;
using Pathwise.Domain.ReviewWindows;

namespace Pathwise.Domain.FactualObservations;

public sealed class FactualObservationGenerator
{
    public const int CurrentVersion = 1;

    public static FactualObservationPolicy Policy { get; } = new(500, 750, 10);

    public FactualObservationResult Generate(
        GameReconstruction reconstruction,
        ReviewWindowDetectionResult selectedWindows)
    {
        ArgumentNullException.ThrowIfNull(reconstruction);
        ArgumentNullException.ThrowIfNull(selectedWindows);
        ValidateDetection(reconstruction, selectedWindows);

        var windows = selectedWindows.Candidates
            .OrderBy(candidate => candidate.RequestedStartTimestampMs)
            .ThenBy(candidate => candidate.RequestedEndTimestampMs)
            .Select(candidate => GenerateWindow(reconstruction, selectedWindows, candidate))
            .ToArray();

        return new(CurrentVersion, Policy, Array.AsReadOnly(windows));
    }

    private static WindowFactualObservations GenerateWindow(
        GameReconstruction reconstruction,
        ReviewWindowDetectionResult detection,
        ReviewWindowCandidate candidate)
    {
        ValidateCandidate(reconstruction, detection, candidate);

        var key = new SelectedReviewWindowKey(
            reconstruction.MatchId,
            reconstruction.ConfiguredParticipantId,
            reconstruction.EnemyResolution.ParticipantId,
            candidate.RequestedStartTimestampMs,
            candidate.RequestedEndTimestampMs,
            reconstruction.ReconstructionVersion,
            detection.DetectorVersion);
        var observations = new List<FactualObservation>();
        var omissions = new List<MetricObservationOmission>();

        if (reconstruction.EnemyResolution.Status == EnemyResolutionStatus.Resolved)
        {
            AddMetricObservation(
                reconstruction,
                candidate,
                key,
                FactualObservationKind.RelativeGoldMovement,
                Policy.MinimumAbsoluteGoldChange,
                observation => observation.TotalGold,
                (observationKey, evidence) => new RelativeGoldObservation(observationKey, evidence),
                observations,
                omissions);
            AddMetricObservation(
                reconstruction,
                candidate,
                key,
                FactualObservationKind.RelativeXpMovement,
                Policy.MinimumAbsoluteXpChange,
                observation => observation.Xp,
                (observationKey, evidence) => new RelativeXpObservation(observationKey, evidence),
                observations,
                omissions);
            AddMetricObservation(
                reconstruction,
                candidate,
                key,
                FactualObservationKind.RelativeJungleCsMovement,
                Policy.MinimumAbsoluteJungleCsChange,
                observation => observation.JungleCs,
                (observationKey, evidence) => new RelativeJungleCsObservation(observationKey, evidence),
                observations,
                omissions);
        }

        var orderedEvents = candidate.Changes.Events
            .Where(@event => @event.TimestampMs > candidate.RequestedStartTimestampMs &&
                @event.TimestampMs <= candidate.RequestedEndTimestampMs)
            .OrderBy(@event => @event.TimestampMs)
            .ThenBy(@event => @event.Source.FrameIndex)
            .ThenBy(@event => @event.Source.EventIndex)
            .ToArray();

        var combatEvents = orderedEvents
            .OfType<ChampionKillEvent>()
            .Where(@event => IsConfiguredPlayerInvolved(@event, reconstruction.ConfiguredParticipantId))
            .DistinctBy(@event => @event.Source)
            .ToArray();
        if (combatEvents.Length > 0)
        {
            observations.Add(new ConfiguredPlayerCombatObservation(
                ObservationKey(key, FactualObservationKind.ConfiguredPlayerCombat),
                Array.AsReadOnly(combatEvents)));
        }

        var objectiveEvents = orderedEvents
            .OfType<EliteMonsterKillEvent>()
            .DistinctBy(@event => @event.Source)
            .ToArray();
        if (objectiveEvents.Length > 0)
        {
            observations.Add(new EliteObjectiveContextObservation(
                ObservationKey(key, FactualObservationKind.EliteObjectiveContext),
                Array.AsReadOnly(objectiveEvents)));
        }

        return new(
            key,
            reconstruction.EnemyResolution,
            observations.AsReadOnly(),
            omissions.AsReadOnly(),
            Array.AsReadOnly(candidate.ReconstructionIssues.ToArray()));
    }

    private static void AddMetricObservation(
        GameReconstruction reconstruction,
        ReviewWindowCandidate candidate,
        SelectedReviewWindowKey windowKey,
        FactualObservationKind kind,
        long threshold,
        Func<PlayerObservation, long> value,
        Func<FactualObservationKey, RelativeMetricEvidence, FactualObservation> create,
        List<FactualObservation> observations,
        List<MetricObservationOmission> omissions)
    {
        var frames = FramesFor(candidate, reconstruction);
        var regression = FindRegression(
            frames,
            reconstruction.ConfiguredParticipantId,
            reconstruction.EnemyResolution.ParticipantId!.Value,
            value);
        if (regression is not null)
        {
            omissions.Add(new(kind, regression));
            return;
        }

        var start = candidate.Changes.StartState;
        var end = candidate.Changes.EndState;
        var configured = new ParticipantMetricEndpoints(
            reconstruction.ConfiguredParticipantId,
            value(start.ConfiguredPlayer.Observation),
            value(end.ConfiguredPlayer.Observation));
        var enemy = new ParticipantMetricEndpoints(
            reconstruction.EnemyResolution.ParticipantId!.Value,
            value(start.EnemyJungler!.Observation),
            value(end.EnemyJungler!.Observation));
        var evidence = new RelativeMetricEvidence(
            new(start.SelectedFrameIndex, start.SelectedFrameTimestampMs),
            new(end.SelectedFrameIndex, end.SelectedFrameTimestampMs),
            configured,
            enemy);

        if (MeetsThreshold(evidence.SignedChange, threshold))
            observations.Add(create(ObservationKey(windowKey, kind), evidence));
    }

    private static IReadOnlyList<FrameObservation> FramesFor(
        ReviewWindowCandidate candidate,
        GameReconstruction reconstruction)
    {
        var observations = reconstruction.Observations;
        var startIndex = FindFrameIndex(observations, candidate.Changes.StartState);
        var endIndex = FindFrameIndex(observations, candidate.Changes.EndState);
        if (startIndex < 0 || endIndex < startIndex)
            return [];
        return observations.Skip(startIndex).Take(endIndex - startIndex + 1).ToArray();
    }

    private static CounterRegressionEvidence? FindRegression(
        IReadOnlyList<FrameObservation> frames,
        int configuredParticipantId,
        int enemyParticipantId,
        Func<PlayerObservation, long> value)
    {
        for (var index = 1; index < frames.Count; index++)
        {
            var configuredPrevious = value(frames[index - 1].ConfiguredPlayer);
            var configuredCurrent = value(frames[index].ConfiguredPlayer);
            if (configuredCurrent < configuredPrevious)
            {
                return new(
                    configuredParticipantId,
                    new(frames[index - 1].FrameIndex, frames[index - 1].TimestampMs),
                    new(frames[index].FrameIndex, frames[index].TimestampMs),
                    configuredPrevious,
                    configuredCurrent);
            }

            var enemyPrevious = value(frames[index - 1].EnemyJungler!);
            var enemyCurrent = value(frames[index].EnemyJungler!);
            if (enemyCurrent < enemyPrevious)
            {
                return new(
                    enemyParticipantId,
                    new(frames[index - 1].FrameIndex, frames[index - 1].TimestampMs),
                    new(frames[index].FrameIndex, frames[index].TimestampMs),
                    enemyPrevious,
                    enemyCurrent);
            }
        }

        return null;
    }

    private static void ValidateDetection(
        GameReconstruction reconstruction,
        ReviewWindowDetectionResult detection)
    {
        if (detection.DetectorVersion != ReviewWindowDetector.CurrentVersion)
            throw new ArgumentException("Factual Observation V1 requires ReviewWindowDetector V2 output.", nameof(detection));
        if (detection.ReconstructionVersion != reconstruction.ReconstructionVersion)
            throw new ArgumentException("Detection and reconstruction versions do not agree.", nameof(detection));
        if (detection.EnemyResolution != reconstruction.EnemyResolution)
            throw new ArgumentException("Detection and reconstruction enemy resolutions do not agree.", nameof(detection));
        if (!detection.SourceIssues.SequenceEqual(reconstruction.SourceDataIssues))
            throw new ArgumentException("Detection and reconstruction source issues do not agree.", nameof(detection));
    }

    private static void ValidateCandidate(
        GameReconstruction reconstruction,
        ReviewWindowDetectionResult detection,
        ReviewWindowCandidate candidate)
    {
        if (candidate.ConfiguredParticipant.ParticipantId != reconstruction.ConfiguredParticipantId ||
            candidate.EnemyResolution != reconstruction.EnemyResolution ||
            candidate.EnemyJungler?.ParticipantId != reconstruction.EnemyResolution.ParticipantId ||
            candidate.RequestedStartTimestampMs != candidate.Changes.RequestedStartTimestampMs ||
            candidate.RequestedEndTimestampMs != candidate.Changes.RequestedEndTimestampMs ||
            candidate.RequestedStartTimestampMs > candidate.RequestedEndTimestampMs ||
            !candidate.ReconstructionIssues.SequenceEqual(reconstruction.SourceDataIssues))
        {
            throw new ArgumentException("Selected review-window identity does not agree with its reconstruction evidence.", nameof(detection));
        }

        var expectedStart = reconstruction.StateAt(candidate.RequestedStartTimestampMs);
        var expectedEnd = reconstruction.StateAt(candidate.RequestedEndTimestampMs);
        ValidateEndpoint(candidate.Changes.StartState, expectedStart, reconstruction, nameof(detection));
        ValidateEndpoint(candidate.Changes.EndState, expectedEnd, reconstruction, nameof(detection));

        if (reconstruction.EnemyResolution.Status == EnemyResolutionStatus.Resolved &&
            (candidate.Changes.StartState.EnemyJungler is null || candidate.Changes.EndState.EnemyJungler is null))
        {
            throw new ArgumentException("Resolved enemy-jungler evidence is missing from a selected window.", nameof(detection));
        }

        var frames = FramesFor(candidate, reconstruction);
        if (frames.Count == 0 ||
            frames[0].FrameIndex != candidate.Changes.StartState.SelectedFrameIndex ||
            frames[^1].FrameIndex != candidate.Changes.EndState.SelectedFrameIndex ||
            reconstruction.EnemyResolution.Status == EnemyResolutionStatus.Resolved && frames.Any(frame => frame.EnemyJungler is null))
        {
            throw new ArgumentException("Selected review-window source frames are inconsistent.", nameof(detection));
        }

        var expectedEvents = reconstruction.EventsBetween(
            candidate.RequestedStartTimestampMs,
            candidate.RequestedEndTimestampMs);
        if (!candidate.Changes.Events.Select(EventIdentity).SequenceEqual(expectedEvents.Select(EventIdentity)))
            throw new ArgumentException("Selected review-window events do not agree with the reconstruction.", nameof(detection));
    }

    private static void ValidateEndpoint(
        GameState actual,
        GameState expected,
        GameReconstruction reconstruction,
        string parameterName)
    {
        if (actual.RequestedTimestampMs != expected.RequestedTimestampMs ||
            actual.SelectedFrameIndex != expected.SelectedFrameIndex ||
            actual.SelectedFrameTimestampMs != expected.SelectedFrameTimestampMs ||
            actual.ConfiguredPlayer.Observation != expected.ConfiguredPlayer.Observation ||
            actual.ConfiguredPlayer.Observation.ParticipantId != reconstruction.ConfiguredParticipantId ||
            actual.EnemyJungler?.Observation != expected.EnemyJungler?.Observation)
        {
            throw new ArgumentException("Selected review-window endpoint evidence does not agree with the reconstruction.", parameterName);
        }
    }

    private static bool IsConfiguredPlayerInvolved(ChampionKillEvent @event, int participantId) =>
        @event.KillerParticipantId == participantId ||
        @event.VictimParticipantId == participantId ||
        @event.AssistingParticipantIds.Contains(participantId);

    private static int FindFrameIndex(IReadOnlyList<FrameObservation> observations, GameState state)
    {
        for (var index = 0; index < observations.Count; index++)
        {
            if (observations[index].FrameIndex == state.SelectedFrameIndex &&
                observations[index].TimestampMs == state.SelectedFrameTimestampMs)
                return index;
        }

        return -1;
    }

    private static (Type Type, long TimestampMs, SourceEventReference Source) EventIdentity(ReconstructionEvent @event) =>
        (@event.GetType(), @event.TimestampMs, @event.Source);

    private static bool MeetsThreshold(long value, long threshold) =>
        value == long.MinValue || Math.Abs(value) >= threshold;

    private static FactualObservationKey ObservationKey(
        SelectedReviewWindowKey window,
        FactualObservationKind kind) =>
        new(window, CurrentVersion, kind);
}
