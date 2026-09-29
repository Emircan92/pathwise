using Pathwise.Application.Reconstruction;
using Pathwise.Application.ReviewWindows;
using Pathwise.Domain.Encounters;
using Pathwise.Domain.FactualObservations;
using Pathwise.Domain.Knowledge;
using Pathwise.Domain.Progression;
using Pathwise.Domain.Reconstruction;
using Pathwise.Domain.ReviewWindows;

namespace Pathwise.Application.Interpretation;

public static class NarrativeInterpretationProjectorV1
{
    public static NarrativeInterpretationInputV1 Project(
        ReconstructionResult<MatchReview> result,
        ReviewWindowCandidate candidate)
    {
        var reconstruction = result.Reconstruction;
        var review = result.Value;
        var key = ReviewPositionSelector.KeyFor(reconstruction, review.WindowDetection, candidate);
        var factual = review.FactualObservations.Windows.Single(value => value.Window == key);
        var encounters = review.Encounters.Windows.Single(value => value.Window == key);
        var progression = review.Progression.Windows.Single(value =>
            value.Window.RequestedStartTimestampMs == candidate.RequestedStartTimestampMs &&
            value.Window.RequestedEndTimestampMs == candidate.RequestedEndTimestampMs);
        var annotations = review.KnowledgeAnnotations.Annotations.Where(value => value.Target.Window == key).ToArray();
        var facts = review.KnowledgeAnnotations.Pack?.Facts.ToDictionary(value => value.Id, StringComparer.Ordinal)
            ?? new Dictionary<string, ObjectiveInitialSpawnFact>(StringComparer.Ordinal);

        var summaries = new List<NarrativeEvidenceV1>
        {
            new NarrativeWindowSelectionEvidenceV1(
                "window:selection",
                Camel(candidate.PrimarySelectionReason),
                SignalKinds(candidate.Signals),
                SignalKinds(candidate.AbsorbedSignals))
        };
        var timeline = new Dictionary<string, NarrativeEvidenceV1>(StringComparer.Ordinal);

        foreach (var observation in factual.Observations)
        {
            switch (observation)
            {
                case RelativeGoldObservation gold:
                    summaries.Add(Metric("observation:relativeGoldMovement", "relativeGoldMovement", gold.Evidence));
                    break;
                case RelativeXpObservation xp:
                    summaries.Add(Metric("observation:relativeXpMovement", "relativeXpMovement", xp.Evidence));
                    break;
                case RelativeJungleCsObservation jungleCs:
                    summaries.Add(Metric("observation:relativeJungleCsMovement", "relativeJungleCsMovement", jungleCs.Evidence));
                    break;
                case ConfiguredPlayerCombatObservation combat:
                {
                    foreach (var value in combat.Events) Add(timeline, Combat(value));
                    summaries.Add(new NarrativeCombatSummaryEvidenceV1(
                        "observation:configuredPlayerCombat",
                        combat.KillCount,
                        combat.DeathCount,
                        combat.AssistCount,
                        combat.DistinctEventCount,
                        combat.Events.Select(value => EventId(value.Source)).ToArray()));
                    break;
                }
                case EliteObjectiveContextObservation objectives:
                {
                    foreach (var value in objectives.Events) Add(timeline, Objective(value));
                    summaries.Add(new NarrativeObjectiveSummaryEvidenceV1(
                        "observation:eliteObjectiveContext",
                        objectives.Events.Select(value => EventId(value.Source)).ToArray()));
                    break;
                }
            }
        }

        foreach (var encounter in encounters.Encounters)
        {
            foreach (var value in encounter.CombatEvents) Add(timeline, Combat(value));
            foreach (var value in encounter.AssociatedObjectiveEvents) Add(timeline, Objective(value));
        }

        foreach (var value in progression.Events) Add(timeline, Progression(value));

        var evidence = new List<NarrativeEvidenceV1>();
        evidence.AddRange(summaries);
        evidence.AddRange(timeline.Values
            .OrderBy(Timestamp)
            .ThenBy(value => Source(value)?.FrameIndex ?? int.MaxValue)
            .ThenBy(value => Source(value)?.EventIndex ?? int.MaxValue)
            .ThenBy(value => value.Id, StringComparer.Ordinal));

        evidence.AddRange(encounters.Encounters.Select(value => new NarrativeEncounterEvidenceV1(
            $"encounter:{value.Id}",
            value.Id,
            value.StartTimestampMs,
            value.EndTimestampMs,
            value.CombatEventCount,
            value.ParticipantIds,
            new(value.ConfiguredPlayerSummary.Involved, value.ConfiguredPlayerSummary.Kills,
                value.ConfiguredPlayerSummary.Deaths, value.ConfiguredPlayerSummary.Assists,
                value.ConfiguredPlayerSummary.DistinctEventCount),
            value.EnemyJunglerInvolved,
            value.CombatEvents.Select(item => EventId(item.Source)).ToArray(),
            value.AssociatedObjectiveEvents.Select(item => EventId(item.Source)).ToArray())));

        foreach (var annotation in annotations)
        {
            if (!facts.TryGetValue(annotation.FactId, out var fact)) continue;
            var targetEventId = annotation.Target.Event is null ? null : EventId(annotation.Target.Event);
            var target = targetEventId ?? "window";
            evidence.Add(new NarrativeKnowledgeAnnotationEvidenceV1(
                $"knowledge:{Camel(annotation.Kind)}:{annotation.FactId}:{target}",
                Camel(annotation.Kind),
                fact.Id,
                Camel(fact.Objective),
                fact.InitialSpawnTimestampMs,
                annotation.Target.Observation is null ? null : Camel(annotation.Target.Observation.Kind),
                targetEventId,
                annotation.RecordedKillTimestampMs,
                fact.Sources.Select(source => new NarrativeKnowledgeSourceV1(source.Title, source.Url.ToString())).ToArray()));
        }

        evidence.AddRange(factual.Omissions.OrderBy(value => value.Kind).Select(value =>
            new NarrativeMetricOmissionEvidenceV1(
                $"omission:{Camel(value.Kind)}",
                Camel(value.Kind),
                "counterRegression",
                value.Regression.ParticipantId,
                Frame(value.Regression.PreviousFrame),
                Frame(value.Regression.CurrentFrame),
                value.Regression.PreviousValue,
                value.Regression.CurrentValue)));

        var issues = candidate.ReconstructionIssues
            .Concat(factual.SourceIssues)
            .Concat(review.WindowDetection.SourceIssues)
            .Distinct()
            .OrderBy(value => value.SourceReference, StringComparer.Ordinal)
            .ThenBy(value => value.Code, StringComparer.Ordinal)
            .ToArray();
        evidence.AddRange(issues.Select((value, index) => new NarrativeSourceIssueEvidenceV1(
            $"issue:{index}:{value.Code}", value.Code, value.SourceReference, value.Explanation, value.HandlingOutcome)));

        if (progression.ContainsGameEnd)
        {
            var outcome = review.Progression.Outcome;
            evidence.Add(new NarrativeOutcomeEvidenceV1(
                "outcome:gameEnd",
                outcome.ConfiguredParticipantId,
                outcome.ConfiguredTeamId,
                OptionalFact(outcome.ConfiguredPlayerWon),
                outcome.ResolvedWinningTeamId,
                OptionalFact(outcome.EndOfGameResult),
                OptionalFact(outcome.MatchEndTimestampMs),
                outcome.TimelineGameEnd is null ? null : EventId(outcome.TimelineGameEnd.Source)));
        }

        EnsureUniqueIds(evidence);
        var patch = review.KnowledgeAnnotations.PatchResolution.Patch;
        var input = new NarrativeInterpretationInputV1(
            NarrativeInterpretationVersions.Input,
            string.Empty,
            new(
                reconstruction.ReconstructionVersion,
                review.WindowDetection.DetectorVersion,
                review.FactualObservations.GeneratorVersion,
                review.KnowledgeAnnotations.GeneratorVersion,
                review.Encounters.DetectorVersion,
                review.Progression.ProjectorVersion),
            reconstruction.ConfiguredParticipantId,
            reconstruction.EnemyResolution.ParticipantId,
            reconstruction.Participants.OrderBy(value => value.ParticipantId).Select(value => new NarrativeParticipantV1(
                value.ParticipantId,
                value.ChampionName,
                value.TeamId,
                Relationship(value, reconstruction))).ToArray(),
            new(
                candidate.RequestedStartTimestampMs,
                candidate.RequestedEndTimestampMs,
                new(candidate.Changes.StartState.SelectedFrameIndex, candidate.Changes.StartState.SelectedFrameTimestampMs),
                new(candidate.Changes.EndState.SelectedFrameIndex, candidate.Changes.EndState.SelectedFrameTimestampMs),
                Camel(candidate.PrimarySelectionReason),
                SignalKinds(candidate.Signals),
                SignalKinds(candidate.AbsorbedSignals)),
            new(patch is null ? null : $"{patch.Major}.{patch.Minor}", Camel(review.KnowledgeAnnotations.Coverage)),
            evidence,
            [
                "Narrative Interpretation V1 receives no periodic position samples, raw map coordinates, inferred paths, or inferred map regions.",
                "Recorded kill encounter times cover recorded kill events, not the full duration of a fight.",
                "The evidence does not establish player intent, decision correctness, cooldowns, vision, communication, mechanical execution, camp availability, objective contestability, or unsupported coordination."
            ]);
        return input with { InputFingerprint = NarrativeInterpretationFingerprint.Compute(input) };
    }

    private static NarrativeMetricObservationEvidenceV1 Metric(string id, string metric, RelativeMetricEvidence value) => new(
        id,
        metric,
        Frame(value.StartFrame),
        Frame(value.EndFrame),
        value.RelativeStart,
        value.RelativeEnd,
        value.SignedChange,
        new(value.ConfiguredPlayer.StartValue, value.ConfiguredPlayer.EndValue),
        new(value.EnemyJungler.StartValue, value.EnemyJungler.EndValue));

    private static NarrativeCombatEventEvidenceV1 Combat(ChampionKillEvent value) => new(
        EventId(value.Source), Source(value.Source), value.TimestampMs, value.KillerParticipantId,
        value.VictimParticipantId, value.AssistingParticipantIds);

    private static NarrativeObjectiveEventEvidenceV1 Objective(EliteMonsterKillEvent value) => new(
        EventId(value.Source), Source(value.Source), value.TimestampMs, value.MonsterType, value.MonsterSubType,
        value.KillerParticipantId, value.AssistingParticipantIds, Team(value.TeamAttribution));

    private static NarrativeEvidenceV1 Progression(ProgressionEvent value) => value switch
    {
        BuildingDestroyedProgressionEvent building => new NarrativeBuildingDestroyedEvidenceV1(
            EventId(building.Source), Source(building.Source), building.TimestampMs, building.BuildingType,
            building.TowerType, building.LaneType, Team(building.StructureOwnerTeam), building.KillerParticipantId,
            building.AssistingParticipantIds),
        RiftHeraldKilledProgressionEvent herald => new NarrativeRiftHeraldKilledEvidenceV1(
            EventId(herald.Source), Source(herald.Source), herald.TimestampMs, herald.KillerParticipantId,
            Team(herald.TeamAttribution), herald.AssistingParticipantIds),
        ItemDestroyedProgressionEvent item => new NarrativeItemDestroyedEvidenceV1(
            EventId(item.Source), Source(item.Source), item.TimestampMs, item.ParticipantId, item.ItemId),
        GameEndedProgressionEvent gameEnd => new NarrativeGameEndedEvidenceV1(
            EventId(gameEnd.Source), Source(gameEnd.Source), gameEnd.TimestampMs, Team(gameEnd.WinningTeam)),
        _ => throw new InvalidOperationException($"Unsupported progression event {value.GetType().Name}.")
    };

    private static void Add(IDictionary<string, NarrativeEvidenceV1> values, NarrativeEvidenceV1 value)
    {
        if (!values.ContainsKey(value.Id)) values.Add(value.Id, value);
    }

    private static void EnsureUniqueIds(IEnumerable<NarrativeEvidenceV1> values)
    {
        var duplicate = values.GroupBy(value => value.Id, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null) throw new InvalidOperationException($"Narrative evidence ID '{duplicate.Key}' is not unique.");
    }

    private static string Relationship(Participant value, GameReconstruction reconstruction)
    {
        if (value.ParticipantId == reconstruction.ConfiguredParticipantId) return "configuredPlayer";
        if (value.ParticipantId == reconstruction.EnemyResolution.ParticipantId) return "enemyJungler";
        var configuredTeam = reconstruction.Participants.Single(item => item.ParticipantId == reconstruction.ConfiguredParticipantId).TeamId;
        return value.TeamId == configuredTeam ? "ally" : "opponent";
    }

    private static IReadOnlyList<string> SignalKinds(IEnumerable<ReviewSignal> values) => values
        .Select(value => Camel(value.Kind))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();

    private static NarrativeMatchFieldV1<T>? OptionalFact<T>(MatchFieldFact<T>? value) where T : struct =>
        value is null ? null : new(value.Value, value.Source.JsonPath);

    private static NarrativeMatchFieldV1<string>? OptionalFact(MatchFieldFact<string>? value) =>
        value is null ? null : new(value.Value, value.Source.JsonPath);

    private static long Timestamp(NarrativeEvidenceV1 value) => value switch
    {
        NarrativeCombatEventEvidenceV1 item => item.TimestampMs,
        NarrativeObjectiveEventEvidenceV1 item => item.TimestampMs,
        NarrativeBuildingDestroyedEvidenceV1 item => item.TimestampMs,
        NarrativeRiftHeraldKilledEvidenceV1 item => item.TimestampMs,
        NarrativeItemDestroyedEvidenceV1 item => item.TimestampMs,
        NarrativeGameEndedEvidenceV1 item => item.TimestampMs,
        _ => long.MaxValue
    };

    private static NarrativeSourceEventV1? Source(NarrativeEvidenceV1 value) => value switch
    {
        NarrativeCombatEventEvidenceV1 item => item.Source,
        NarrativeObjectiveEventEvidenceV1 item => item.Source,
        NarrativeBuildingDestroyedEvidenceV1 item => item.Source,
        NarrativeRiftHeraldKilledEvidenceV1 item => item.Source,
        NarrativeItemDestroyedEvidenceV1 item => item.Source,
        NarrativeGameEndedEvidenceV1 item => item.Source,
        _ => null
    };

    private static NarrativeSourceEventV1 Source(SourceEventReference value) => new(value.FrameIndex, value.EventIndex);
    private static NarrativeSourceFrameV1 Frame(SourceFrameReference value) => new(value.FrameIndex, value.TimestampMs);
    private static string EventId(SourceEventReference value) => $"event:{value.FrameIndex}:{value.EventIndex}";
    private static NarrativeTeamAttributionV1 Team(TeamAttribution value) => new(value.Kind.ToString(), value.SuppliedTeamId, value.ResolvedTeamId, value.DiagnosticReason);

    private static string Camel<T>(T value) where T : struct, Enum
    {
        var text = value.ToString();
        return char.ToLowerInvariant(text[0]) + text[1..];
    }
}
