using Pathwise.Domain.Reconstruction;

namespace Pathwise.Domain.ReviewWindows;

public sealed class ReviewWindowDetector
{
    public const int CurrentVersion = 1;

    public ReviewWindowDetectionResult Detect(GameReconstruction reconstruction, ReviewWindowOptions options)
    {
        ArgumentNullException.ThrowIfNull(reconstruction);
        ArgumentNullException.ThrowIfNull(options);
        Validate(options);

        var skipped = new List<SkippedMetricComparison>();
        var seeds = new List<Seed>();
        seeds.AddRange(CreateMetricSeeds(reconstruction, options, skipped));
        seeds.AddRange(CreateEventSeeds(reconstruction, options));
        var ordered = seeds.OrderBy(x => x, SeedComparer.Instance).ToList();

        var selected = Select(ordered, options);
        var configured = reconstruction.Participants.Single(x => x.ParticipantId == reconstruction.ConfiguredParticipantId);
        var enemy = reconstruction.EnemyResolution.ParticipantId is { } enemyId
            ? reconstruction.Participants.Single(x => x.ParticipantId == enemyId)
            : null;

        var candidates = selected.Windows
            .Select(window => BuildCandidate(reconstruction, configured, enemy, window))
            .OrderBy(x => x.RequestedStartTimestampMs)
            .ThenBy(x => x.RequestedEndTimestampMs)
            .ToArray();

        return new(
            candidates,
            CurrentVersion,
            reconstruction.ReconstructionVersion,
            options,
            reconstruction.EnemyResolution,
            reconstruction.SourceDataIssues,
            skipped,
            selected.Suppressed,
            selected.Capped);
    }

    private static IEnumerable<Seed> CreateMetricSeeds(
        GameReconstruction reconstruction,
        ReviewWindowOptions options,
        List<SkippedMetricComparison> skipped)
    {
        if (reconstruction.EnemyResolution.Status != EnemyResolutionStatus.Resolved)
            yield break;

        var observations = reconstruction.Observations;
        for (var endIndex = 0; endIndex < observations.Count; endIndex++)
        {
            var end = observations[endIndex];
            var target = end.TimestampMs - options.MetricLookbackMs;
            var startIndex = -1;
            for (var candidate = endIndex - 1; candidate >= 0; candidate--)
            {
                if (observations[candidate].TimestampMs <= target)
                {
                    startIndex = candidate;
                    break;
                }
            }

            if (startIndex < 0)
                continue;

            var start = observations[startIndex];
            if (HasLargeGap(observations, startIndex, endIndex, options.MaximumAdjacentFrameGapMs))
            {
                AddGapSkip(skipped, ReviewSignalKind.GoldDifferenceChange, start, end, reconstruction.ConfiguredParticipantId);
                AddGapSkip(skipped, ReviewSignalKind.XpDifferenceChange, start, end, reconstruction.ConfiguredParticipantId);
                continue;
            }

            var signals = new List<ReviewSignal>();
            AddMetricSignal(reconstruction, observations, startIndex, endIndex, ReviewSignalKind.GoldDifferenceChange,
                options.GoldChangeThreshold, x => x.TotalGold, signals, skipped);
            AddMetricSignal(reconstruction, observations, startIndex, endIndex, ReviewSignalKind.XpDifferenceChange,
                options.XpChangeThreshold, x => x.Xp, signals, skipped);
            if (signals.Count > 0)
                yield return new Seed(start.TimestampMs, end.TimestampMs, signals, ReasonFor(signals));
        }
    }

    private static IEnumerable<Seed> CreateEventSeeds(GameReconstruction reconstruction, ReviewWindowOptions options)
    {
        var playerId = reconstruction.ConfiguredParticipantId;
        var involvedKills = reconstruction.Events.OfType<ChampionKillEvent>()
            .Where(x => IsPlayerInvolved(x, playerId))
            .ToArray();
        var emittedCombatClusters = new HashSet<string>(StringComparer.Ordinal);

        foreach (var death in involvedKills.Where(x => x.VictimParticipantId == playerId))
        {
            var signal = EventSignal(ReviewSignalKind.ConfiguredPlayerDeath, death.TimestampMs, death.Source);
            yield return PaddedSeed(reconstruction, options, death.TimestampMs, [signal], ReviewSelectionReason.ConfiguredPlayerDeath);
        }

        foreach (var objective in reconstruction.Events.OfType<EliteMonsterKillEvent>())
        {
            var signal = EventSignal(ReviewSignalKind.EliteMonsterKill, objective.TimestampMs, objective.Source);
            yield return PaddedSeed(reconstruction, options, objective.TimestampMs, [signal], ReviewSelectionReason.EliteMonsterKill);
        }

        foreach (var endingEvent in involvedKills)
        {
            var lowerBound = endingEvent.TimestampMs - options.CombatLookbackMs;
            var contributors = involvedKills
                .Where(x => x.TimestampMs >= lowerBound && x.TimestampMs <= endingEvent.TimestampMs)
                .DistinctBy(x => x.Source)
                .ToArray();
            if (contributors.Length < options.CombatMinimumDistinctEvents)
                continue;

            var contributorKey = string.Join('|', contributors
                .Select(x => x.Source)
                .OrderBy(x => x.FrameIndex)
                .ThenBy(x => x.EventIndex)
                .Select(x => $"{x.FrameIndex}:{x.EventIndex}"));
            if (!emittedCombatClusters.Add(contributorKey))
                continue;

            var first = contributors.Min(x => x.TimestampMs);
            var last = contributors.Max(x => x.TimestampMs);
            var signal = new ReviewSignal(
                ReviewSignalKind.ConcentratedPlayerCombat,
                first,
                last,
                [],
                contributors.Select(x => x.Source).OrderBy(x => x.FrameIndex).ThenBy(x => x.EventIndex).ToArray(),
                null,
                null,
                null,
                options.CombatMinimumDistinctEvents,
                ReviewSignalDirection.NotApplicable,
                contributors.Length);
            yield return new Seed(
                Math.Max(reconstruction.AvailableFromMs, first - options.EventContextPaddingMs),
                Math.Min(reconstruction.AvailableToMs, last + options.EventContextPaddingMs),
                [signal],
                ReviewSelectionReason.ConcentratedPlayerCombat);
        }
    }

    private static void AddMetricSignal(
        GameReconstruction reconstruction,
        IReadOnlyList<FrameObservation> observations,
        int startIndex,
        int endIndex,
        ReviewSignalKind kind,
        long threshold,
        Func<PlayerObservation, long> value,
        List<ReviewSignal> signals,
        List<SkippedMetricComparison> skipped)
    {
        var start = observations[startIndex];
        var end = observations[endIndex];
        var configuredRegression = FindRegression(observations, startIndex, endIndex, x => x.ConfiguredPlayer, value);
        if (configuredRegression is { } playerRegression)
        {
            skipped.Add(new(kind, Frame(start), Frame(end), SkippedComparisonReason.ConfiguredPlayerCounterRegression,
                reconstruction.ConfiguredParticipantId, playerRegression.Previous, playerRegression.Current));
            return;
        }

        var enemyRegression = FindRegression(observations, startIndex, endIndex, x => x.EnemyJungler!, value);
        if (enemyRegression is { } opposingRegression)
        {
            skipped.Add(new(kind, Frame(start), Frame(end), SkippedComparisonReason.EnemyCounterRegression,
                reconstruction.EnemyResolution.ParticipantId!.Value, opposingRegression.Previous, opposingRegression.Current));
            return;
        }

        var startRelative = value(start.ConfiguredPlayer) - value(start.EnemyJungler!);
        var endRelative = value(end.ConfiguredPlayer) - value(end.EnemyJungler!);
        var change = endRelative - startRelative;
        if (Math.Abs(change) < threshold)
            return;

        signals.Add(new(
            kind,
            start.TimestampMs,
            end.TimestampMs,
            [Frame(start), Frame(end)],
            [],
            startRelative,
            endRelative,
            change,
            threshold,
            Direction(change),
            0));
    }

    private static SelectionResult Select(List<Seed> remaining, ReviewWindowOptions options)
    {
        var windows = new List<SelectedSeed>();
        var suppressed = new List<SuppressedReviewSeed>();

        while (remaining.Count > 0 && windows.Count < options.MaximumSelectedWindows)
        {
            var primary = remaining[0];
            remaining.RemoveAt(0);
            var selected = new SelectedSeed(windows.Count + 1, primary);

            var absorbedAny = true;
            while (absorbedAny)
            {
                absorbedAny = false;
                foreach (var candidate in remaining.ToArray())
                {
                    var unionStart = Math.Min(selected.Start, candidate.Start);
                    var unionEnd = Math.Max(selected.End, candidate.End);
                    if (Overlap(selected.Start, selected.End, candidate.Start, candidate.End) <= 0 ||
                        unionEnd - unionStart > options.MaximumMergedDurationMs ||
                        windows.Any(previous => IsDuplicate(unionStart, unionEnd, previous.Start, previous.End, options.DuplicateOverlapFraction)) ||
                        windows.Any(previous => candidate.TriggerReferences.Any(previous.AssignedTriggerReferences.Contains)))
                        continue;

                    selected.Absorb(candidate);
                    remaining.Remove(candidate);
                    absorbedAny = true;
                }
            }

            windows.Add(selected);
            foreach (var candidate in remaining.ToArray())
            {
                var duplicate = IsDuplicate(selected.Start, selected.End, candidate.Start, candidate.End, options.DuplicateOverlapFraction);
                var assigned = candidate.TriggerReferences.Any(selected.AssignedTriggerReferences.Contains);
                if (!duplicate && !assigned)
                    continue;

                var diagnostic = Diagnostic(selected.Rank, assigned ? SuppressionReason.TriggerAlreadyAssigned : SuppressionReason.DuplicateOverlap, candidate);
                selected.Suppressed.Add(diagnostic);
                suppressed.Add(diagnostic);
                remaining.Remove(candidate);
            }
        }

        var capped = remaining.Select(x => Diagnostic(0, SuppressionReason.CandidateCap, x)).ToArray();
        return new(windows, suppressed, capped);
    }

    private static ReviewWindowCandidate BuildCandidate(
        GameReconstruction reconstruction,
        Participant configured,
        Participant? enemy,
        SelectedSeed selected)
    {
        var changes = reconstruction.Changes(selected.Start, selected.End);
        return new(
            selected.Rank,
            selected.PrimaryReason,
            selected.Start,
            selected.End,
            configured,
            enemy,
            reconstruction.EnemyResolution,
            changes,
            Relative(changes),
            selected.Signals.ToArray(),
            selected.AbsorbedSignals.ToArray(),
            selected.AssignedTriggerReferences.OrderBy(x => x.FrameIndex).ThenBy(x => x.EventIndex).ToArray(),
            changes.Events,
            reconstruction.SourceDataIssues,
            selected.Suppressed.ToArray());
    }

    private static RelativeWindowEvidence? Relative(GameChanges changes)
    {
        if (changes.StartState.EnemyJungler is null || changes.EndState.EnemyJungler is null)
            return null;

        var start = Relative(changes.StartState.ConfiguredPlayer, changes.StartState.EnemyJungler);
        var end = Relative(changes.EndState.ConfiguredPlayer, changes.EndState.EnemyJungler);
        return new(start, end, new(
            end.TotalGold - start.TotalGold,
            end.Xp - start.Xp,
            end.Level - start.Level,
            end.JungleCs - start.JungleCs,
            end.LaneCs - start.LaneCs,
            end.Kills - start.Kills,
            end.Deaths - start.Deaths,
            end.Assists - start.Assists));
    }

    private static RelativePlayerValues Relative(PlayerState configured, PlayerState enemy) => new(
        configured.Observation.TotalGold - enemy.Observation.TotalGold,
        configured.Observation.Xp - enemy.Observation.Xp,
        configured.Observation.Level - enemy.Observation.Level,
        configured.Observation.JungleCs - enemy.Observation.JungleCs,
        configured.Observation.LaneCs - enemy.Observation.LaneCs,
        configured.Kills - enemy.Kills,
        configured.Deaths - enemy.Deaths,
        configured.Assists - enemy.Assists);

    private static ReviewSignal EventSignal(ReviewSignalKind kind, long timestamp, SourceEventReference source) =>
        new(kind, timestamp, timestamp, [], [source], null, null, null, null, ReviewSignalDirection.NotApplicable, 1);

    private static Seed PaddedSeed(GameReconstruction reconstruction, ReviewWindowOptions options, long timestamp, IReadOnlyList<ReviewSignal> signals, ReviewSelectionReason reason) =>
        new(Math.Max(reconstruction.AvailableFromMs, timestamp - options.EventContextPaddingMs),
            Math.Min(reconstruction.AvailableToMs, timestamp + options.EventContextPaddingMs), signals, reason);

    private static ReviewSelectionReason ReasonFor(IReadOnlyList<ReviewSignal> signals) =>
        signals.Any(x => x.Kind == ReviewSignalKind.GoldDifferenceChange) && signals.Any(x => x.Kind == ReviewSignalKind.XpDifferenceChange)
            ? ReviewSelectionReason.GoldAndXpChange
            : signals.Any(x => x.Kind == ReviewSignalKind.GoldDifferenceChange)
                ? ReviewSelectionReason.GoldChange
                : ReviewSelectionReason.XpChange;

    private static (long Previous, long Current)? FindRegression(
        IReadOnlyList<FrameObservation> observations,
        int startIndex,
        int endIndex,
        Func<FrameObservation, PlayerObservation> player,
        Func<PlayerObservation, long> value)
    {
        for (var index = startIndex + 1; index <= endIndex; index++)
        {
            var previous = value(player(observations[index - 1]));
            var current = value(player(observations[index]));
            if (current < previous)
                return (previous, current);
        }
        return null;
    }

    private static bool HasLargeGap(IReadOnlyList<FrameObservation> observations, int startIndex, int endIndex, long maximumGapMs)
    {
        for (var index = startIndex + 1; index <= endIndex; index++)
            if (observations[index].TimestampMs - observations[index - 1].TimestampMs > maximumGapMs)
                return true;
        return false;
    }

    private static void AddGapSkip(List<SkippedMetricComparison> skipped, ReviewSignalKind kind, FrameObservation start, FrameObservation end, int playerId) =>
        skipped.Add(new(kind, Frame(start), Frame(end), SkippedComparisonReason.AdjacentFrameGap, playerId, null, null));

    private static bool IsPlayerInvolved(ChampionKillEvent kill, int playerId) =>
        kill.KillerParticipantId == playerId || kill.VictimParticipantId == playerId || kill.AssistingParticipantIds.Contains(playerId);

    private static SourceFrameReference Frame(FrameObservation observation) => new(observation.FrameIndex, observation.TimestampMs);
    private static ReviewSignalDirection Direction(long change) => change > 0 ? ReviewSignalDirection.Increase : change < 0 ? ReviewSignalDirection.Decrease : ReviewSignalDirection.Unchanged;
    private static long Overlap(long leftStart, long leftEnd, long rightStart, long rightEnd) => Math.Max(0, Math.Min(leftEnd, rightEnd) - Math.Max(leftStart, rightStart));
    private static bool IsDuplicate(long leftStart, long leftEnd, long rightStart, long rightEnd, double fraction)
    {
        var shorter = Math.Min(leftEnd - leftStart, rightEnd - rightStart);
        return shorter == 0
            ? leftStart == rightStart
            : Overlap(leftStart, leftEnd, rightStart, rightEnd) >= shorter * fraction;
    }

    private static SuppressedReviewSeed Diagnostic(int rank, SuppressionReason reason, Seed seed) =>
        new(rank, reason, seed.Start, seed.End, seed.Signals.ToArray());

    private static void Validate(ReviewWindowOptions options)
    {
        if (options.MetricLookbackMs <= 0 || options.GoldChangeThreshold <= 0 || options.XpChangeThreshold <= 0 ||
            options.CombatLookbackMs <= 0 || options.CombatMinimumDistinctEvents <= 0 || options.EventContextPaddingMs < 0 ||
            options.MaximumMergedDurationMs <= 0 || options.DuplicateOverlapFraction is <= 0 or > 1 ||
            options.MaximumSelectedWindows <= 0 || options.MaximumAdjacentFrameGapMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Review-window options must contain valid positive limits and an overlap fraction in (0, 1].");
    }

    private sealed class Seed(long start, long end, IReadOnlyList<ReviewSignal> signals, ReviewSelectionReason primaryReason)
    {
        public long Start { get; } = start;
        public long End { get; } = end;
        public IReadOnlyList<ReviewSignal> Signals { get; } = signals;
        public ReviewSelectionReason PrimaryReason { get; } = primaryReason;
        public IEnumerable<SourceEventReference> TriggerReferences => Signals.SelectMany(x => x.SourceEvents).Distinct();
    }

    private sealed class SelectedSeed(int rank, Seed primary)
    {
        public int Rank { get; } = rank;
        public long Start { get; private set; } = primary.Start;
        public long End { get; private set; } = primary.End;
        public ReviewSelectionReason PrimaryReason { get; } = primary.PrimaryReason;
        public List<ReviewSignal> Signals { get; } = [.. primary.Signals];
        public List<ReviewSignal> AbsorbedSignals { get; } = [];
        public HashSet<SourceEventReference> AssignedTriggerReferences { get; } = [.. primary.TriggerReferences];
        public List<SuppressedReviewSeed> Suppressed { get; } = [];

        public void Absorb(Seed seed)
        {
            Start = Math.Min(Start, seed.Start);
            End = Math.Max(End, seed.End);
            Signals.AddRange(seed.Signals);
            AbsorbedSignals.AddRange(seed.Signals);
            AssignedTriggerReferences.UnionWith(seed.TriggerReferences);
        }
    }

    private sealed record SelectionResult(
        IReadOnlyList<SelectedSeed> Windows,
        IReadOnlyList<SuppressedReviewSeed> Suppressed,
        IReadOnlyList<SuppressedReviewSeed> Capped);

    private sealed class SeedComparer : IComparer<Seed>
    {
        public static SeedComparer Instance { get; } = new();

        public int Compare(Seed? left, Seed? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;
            var result = Priority(left.PrimaryReason).CompareTo(Priority(right.PrimaryReason));
            if (result != 0) return result;

            result = MetricMagnitude(right, ReviewSignalKind.GoldDifferenceChange).CompareTo(MetricMagnitude(left, ReviewSignalKind.GoldDifferenceChange));
            if (result != 0 && left.PrimaryReason is ReviewSelectionReason.GoldAndXpChange or ReviewSelectionReason.GoldChange) return result;
            result = MetricMagnitude(right, ReviewSignalKind.XpDifferenceChange).CompareTo(MetricMagnitude(left, ReviewSignalKind.XpDifferenceChange));
            if (result != 0 && left.PrimaryReason == ReviewSelectionReason.XpChange) return result;
            result = EventCount(right).CompareTo(EventCount(left));
            if (result != 0 && left.PrimaryReason == ReviewSelectionReason.ConcentratedPlayerCombat) return result;
            result = left.Start.CompareTo(right.Start);
            if (result != 0) return result;
            result = left.End.CompareTo(right.End);
            if (result != 0) return result;
            return SourceKey(left).CompareTo(SourceKey(right));
        }

        private static int Priority(ReviewSelectionReason reason) => reason switch
        {
            ReviewSelectionReason.GoldAndXpChange => 1,
            ReviewSelectionReason.GoldChange => 2,
            ReviewSelectionReason.XpChange => 3,
            ReviewSelectionReason.ConfiguredPlayerDeath => 4,
            ReviewSelectionReason.ConcentratedPlayerCombat => 5,
            _ => 6
        };

        private static long MetricMagnitude(Seed seed, ReviewSignalKind kind) =>
            seed.Signals.Where(x => x.Kind == kind).Select(x => Math.Abs(x.SignedChange ?? 0)).DefaultIfEmpty().Max();
        private static int EventCount(Seed seed) => seed.Signals.Select(x => x.DistinctEventCount).DefaultIfEmpty().Max();
        private static (int Frame, int Event) SourceKey(Seed seed)
        {
            var eventSource = seed.Signals.SelectMany(x => x.SourceEvents).OrderBy(x => x.FrameIndex).ThenBy(x => x.EventIndex).FirstOrDefault();
            if (eventSource is not null) return (eventSource.FrameIndex, eventSource.EventIndex);
            var frame = seed.Signals.SelectMany(x => x.SourceFrames).OrderBy(x => x.FrameIndex).FirstOrDefault();
            return frame is null ? (int.MaxValue, int.MaxValue) : (frame.FrameIndex, -1);
        }
    }
}
