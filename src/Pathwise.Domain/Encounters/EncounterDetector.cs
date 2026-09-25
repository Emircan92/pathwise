using System.Security.Cryptography;
using System.Text.Json;
using Pathwise.Domain.FactualObservations;
using Pathwise.Domain.Reconstruction;
using Pathwise.Domain.ReviewWindows;

namespace Pathwise.Domain.Encounters;

public sealed class EncounterDetector
{
    public const int CurrentVersion = 1;
    public static EncounterPolicy Policy { get; } = new(5_000, 20_000, 10_000, 2_500, 1, 2, 60_000, 5_000, 30_000, 2_500);

    public EncounterDetectionResult Detect(GameReconstruction reconstruction, ReviewWindowDetectionResult selectedWindows)
    {
        var knownParticipants = reconstruction.Participants.Select(p => p.ParticipantId).ToHashSet();
        var windows = selectedWindows.Candidates.Select(candidate =>
        {
            var key = new SelectedReviewWindowKey(reconstruction.MatchId, reconstruction.ConfiguredParticipantId,
                reconstruction.EnemyResolution.ParticipantId,
                candidate.RequestedStartTimestampMs, candidate.RequestedEndTimestampMs,
                reconstruction.ReconstructionVersion, selectedWindows.DetectorVersion);
            var events = reconstruction.Events.OfType<ChampionKillEvent>()
                .Where(e => e.TimestampMs > key.RequestedStartTimestampMs && e.TimestampMs <= key.RequestedEndTimestampMs)
                .OrderBy(e => e.TimestampMs).ThenBy(e => e.Source.FrameIndex).ThenBy(e => e.Source.EventIndex)
                .DistinctBy(e => e.Source).ToArray();
            var objectives = reconstruction.Events.OfType<EliteMonsterKillEvent>()
                .Where(e => e.TimestampMs > key.RequestedStartTimestampMs && e.TimestampMs <= key.RequestedEndTimestampMs)
                .OrderBy(e => e.TimestampMs).ThenBy(e => e.Source.FrameIndex).ThenBy(e => e.Source.EventIndex)
                .DistinctBy(e => e.Source).ToArray();
            return new WindowEncounters(key, Group(key, events, objectives, knownParticipants, reconstruction.EnemyResolution.Status));
        }).ToArray();
        return new(CurrentVersion, Policy, windows);
    }

    private static IReadOnlyList<Encounter> Group(SelectedReviewWindowKey key, ChampionKillEvent[] events,
        EliteMonsterKillEvent[] objectives, HashSet<int> known, EnemyResolutionStatus enemyStatus)
    {
        var participants = events.Select(e => Participants(e, known)).ToArray();
        var edges = new List<Edge>();
        for (var i = 0; i < events.Length; i++)
            for (var j = i + 1; j < events.Length; j++)
            {
                var difference = events[j].TimestampMs - events[i].TimestampMs;
                var overlap = participants[i].Intersect(participants[j]).Count();
                long? distance = events[i].Position is { } a && events[j].Position is { } b ? SquaredDistance(a, b) : null;
                EncounterLinkTier? tier = distance is { } squared
                    ? squared <= Square(Policy.LinkDistanceUnits) && difference <= Policy.LocalTimeMs ? EncounterLinkTier.Local
                        : squared <= Square(Policy.LinkDistanceUnits) && difference <= Policy.ExtendedTimeMs && overlap >= Policy.ExtendedOverlap ? EncounterLinkTier.Extended : null
                    : difference <= Policy.MissingPositionTimeMs && overlap >= Policy.MissingPositionOverlap ? EncounterLinkTier.MissingPositionFallback : null;
                if (tier is { } value) edges.Add(new(i, j, value, difference, distance, overlap));
            }
        edges = edges.OrderBy(e => e.Tier).ThenBy(e => e.TimeDifference).ThenBy(e => e.Distance ?? long.MaxValue)
            .ThenByDescending(e => e.Overlap).ThenBy(e => events[e.Earlier].TimestampMs)
            .ThenBy(e => events[e.Earlier].Source.FrameIndex).ThenBy(e => events[e.Earlier].Source.EventIndex)
            .ThenBy(e => events[e.Later].TimestampMs).ThenBy(e => events[e.Later].Source.FrameIndex)
            .ThenBy(e => events[e.Later].Source.EventIndex).ToList();

        var roots = Enumerable.Range(0, events.Length).ToArray();
        var members = Enumerable.Range(0, events.Length).ToDictionary(i => i, i => new List<int> { i });
        var provenance = new List<Edge>();
        var positionedIdentity = new Dictionary<int, int>();
        foreach (var edge in edges.Where(e => e.Tier != EncounterLinkTier.MissingPositionFallback)) Merge(edge);
        foreach (var component in members)
            if (component.Value.Any(i => events[i].Position is not null)) positionedIdentity[component.Key] = component.Key;
        foreach (var edge in edges.Where(e => e.Tier == EncounterLinkTier.MissingPositionFallback)) Merge(edge);

        return members.Values.Select(indices =>
        {
            var combat = indices.OrderBy(i => i).Select(i => events[i]).ToArray();
            var ids = indices.SelectMany(i => participants[i]).Distinct().Order().ToArray();
            var player = key.ConfiguredParticipantId;
            var kills = combat.Count(e => e.KillerParticipantId == player);
            var deaths = combat.Count(e => e.VictimParticipantId == player);
            var assists = combat.Count(e => e.AssistingParticipantIds.Contains(player));
            var distinct = combat.Count(e => e.KillerParticipantId == player || e.VictimParticipantId == player || e.AssistingParticipantIds.Contains(player));
            var associated = objectives.Where(o => o.Position is { } objectivePosition && combat.Any(e =>
                e.Position is { } combatPosition && Math.Abs(e.TimestampMs - o.TimestampMs) <= Policy.ObjectiveTimeMs &&
                SquaredDistance(combatPosition, objectivePosition) <= Square(Policy.ObjectiveDistanceUnits))).ToArray();
            var sourceSet = indices.ToHashSet();
            return new Encounter(Identity(key, combat, enemyStatus), combat[0].TimestampMs, combat[^1].TimestampMs,
                combat[^1].TimestampMs - combat[0].TimestampMs, combat.Length, ids, ids.Length,
                new(distinct > 0, kills, deaths, assists, distinct), enemyStatus == EnemyResolutionStatus.Resolved && key.EnemyParticipantId is { } enemy ? ids.Contains(enemy) : null,
                combat, associated, provenance.Where(e => sourceSet.Contains(e.Earlier) && sourceSet.Contains(e.Later))
                    .Select(e => new EncounterGroupingEdge(events[e.Earlier].Source, events[e.Later].Source, e.Tier)).ToArray());
        }).OrderBy(e => e.StartTimestampMs).ThenBy(e => e.EndTimestampMs)
            .ThenBy(e => e.CombatEvents[0].Source.FrameIndex).ThenBy(e => e.CombatEvents[0].Source.EventIndex).ToArray();

        void Merge(Edge edge)
        {
            var left = Find(edge.Earlier);
            var right = Find(edge.Later);
            if (left == right) return;
            if (edge.Tier == EncounterLinkTier.MissingPositionFallback && positionedIdentity.ContainsKey(left) && positionedIdentity.ContainsKey(right)) return;
            var union = members[left].Concat(members[right]).ToArray();
            if (events[union.Max(i => i)].TimestampMs - events[union.Min(i => i)].TimestampMs > Policy.MaximumSpanMs) return;
            var located = union.Where(i => events[i].Position is not null).ToArray();
            for (var i = 0; i < located.Length; i++)
                for (var j = i + 1; j < located.Length; j++)
                    if (SquaredDistance(events[located[i]].Position!, events[located[j]].Position!) > Square(Policy.MaximumDiameterUnits)) return;
            roots[right] = left;
            members[left].AddRange(members[right]);
            members.Remove(right);
            if (positionedIdentity.ContainsKey(right)) positionedIdentity[left] = positionedIdentity[right];
            provenance.Add(edge);
        }

        int Find(int index)
        {
            while (roots[index] != index) index = roots[index];
            return index;
        }
    }

    private static HashSet<int> Participants(ChampionKillEvent e, HashSet<int> known)
    {
        var result = new HashSet<int>();
        if (e.KillerParticipantId is { } killer && known.Contains(killer)) result.Add(killer);
        if (known.Contains(e.VictimParticipantId)) result.Add(e.VictimParticipantId);
        foreach (var assist in e.AssistingParticipantIds)
            if (known.Contains(assist)) result.Add(assist);
        return result;
    }

    private static long Square(long value) => checked(value * value);
    private static long SquaredDistance(Position a, Position b)
    {
        var dx = (long)a.X - b.X;
        var dy = (long)a.Y - b.Y;
        // Coordinates are int32, so each squared term fits in unsigned 64 bits. Saturation is sufficient for threshold comparison.
        var squared = (decimal)dx * dx + (decimal)dy * dy;
        return squared > long.MaxValue ? long.MaxValue : (long)squared;
    }

    private static string Identity(SelectedReviewWindowKey key, IReadOnlyList<ChampionKillEvent> events, EnemyResolutionStatus enemyStatus)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            writer.WriteStringValue(key.MatchId);
            writer.WriteNumberValue(key.ConfiguredParticipantId);
            if (enemyStatus == EnemyResolutionStatus.Resolved && key.EnemyParticipantId is { } enemy) writer.WriteNumberValue(enemy); else writer.WriteNullValue();
            writer.WriteNumberValue(key.RequestedStartTimestampMs);
            writer.WriteNumberValue(key.RequestedEndTimestampMs);
            writer.WriteNumberValue(key.ReconstructionVersion);
            writer.WriteNumberValue(key.DetectorVersion);
            writer.WriteNumberValue(CurrentVersion);
            writer.WriteStartArray();
            foreach (var e in events.OrderBy(e => e.Source.FrameIndex).ThenBy(e => e.Source.EventIndex))
            {
                writer.WriteStartArray(); writer.WriteNumberValue(e.Source.FrameIndex); writer.WriteNumberValue(e.Source.EventIndex); writer.WriteEndArray();
            }
            writer.WriteEndArray(); writer.WriteEndArray();
        }
        return "enc-v1-" + Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    private sealed record Edge(int Earlier, int Later, EncounterLinkTier Tier, long TimeDifference, long? Distance, int Overlap);
}
