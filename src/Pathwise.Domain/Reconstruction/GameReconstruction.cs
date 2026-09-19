namespace Pathwise.Domain.Reconstruction;

public sealed class GameReconstruction
{
    public const int CurrentVersion = 1;
    private readonly IReadOnlyDictionary<int, Participant> _participantsById;

    public GameReconstruction(ReconstructionInput input)
    {
        MatchId = input.MatchId;
        Patch = input.Patch;
        QueueId = input.QueueId;
        MapId = input.MapId;
        ReportedDurationSeconds = input.ReportedDurationSeconds;
        Participants = input.Participants.ToArray();
        ConfiguredParticipantId = input.ConfiguredParticipantId;
        EnemyResolution = input.EnemyResolution;
        Observations = input.Observations.OrderBy(x => x.TimestampMs).ToArray();
        Events = input.Events.OrderBy(x => x.TimestampMs).ThenBy(x => x.Source.FrameIndex).ThenBy(x => x.Source.EventIndex).ToArray();
        SourceDataIssues = input.SourceDataIssues.ToArray();
        _participantsById = Participants.ToDictionary(x => x.ParticipantId);

        if (Observations.Count == 0) throw new ArgumentException("A reconstruction requires observations.", nameof(input));
    }

    public string MatchId { get; }
    public int ReconstructionVersion => CurrentVersion;
    public string Patch { get; }
    public int QueueId { get; }
    public int MapId { get; }
    public int ReportedDurationSeconds { get; }
    public IReadOnlyList<Participant> Participants { get; }
    public int ConfiguredParticipantId { get; }
    public EnemyJunglerResolution EnemyResolution { get; }
    public IReadOnlyList<FrameObservation> Observations { get; }
    public IReadOnlyList<ReconstructionEvent> Events { get; }
    public IReadOnlyList<SourceDataIssue> SourceDataIssues { get; }
    public long AvailableFromMs => Observations[0].TimestampMs;
    public long AvailableToMs => Observations[^1].TimestampMs;

    public GameState StateAt(long timestampMs)
    {
        EnsureInRange(timestampMs);
        var frame = Observations.Last(x => x.TimestampMs <= timestampMs);
        return new(
            timestampMs,
            frame.FrameIndex,
            frame.TimestampMs,
            timestampMs,
            BuildPlayerState(frame.ConfiguredPlayer, timestampMs),
            frame.EnemyJungler is null ? null : BuildPlayerState(frame.EnemyJungler, timestampMs),
            EnemyResolution);
    }

    public GameChanges Changes(long fromMs, long toMs)
    {
        if (fromMs > toMs)
            throw new ReconstructionQueryException("reversed_interval", "fromMs must be less than or equal to toMs.", AvailableFromMs, AvailableToMs);

        var start = StateAt(fromMs);
        var end = StateAt(toMs);
        var events = Events.Where(x => x.TimestampMs > fromMs && x.TimestampMs <= toMs).ToArray();
        return new(
            fromMs,
            toMs,
            start,
            end,
            Delta(start.ConfiguredPlayer, end.ConfiguredPlayer),
            start.EnemyJungler is null || end.EnemyJungler is null ? null : Delta(start.EnemyJungler, end.EnemyJungler),
            events);
    }

    public IReadOnlyList<ReconstructionEvent> EventsBetween(long fromExclusiveMs, long toInclusiveMs) =>
        Events.Where(x => x.TimestampMs > fromExclusiveMs && x.TimestampMs <= toInclusiveMs).ToArray();

    private PlayerState BuildPlayerState(PlayerObservation observation, long cutoffMs)
    {
        var kills = 0;
        var deaths = 0;
        var assists = 0;
        var contributions = new List<CombatEventContribution>();
        foreach (var kill in Events.OfType<ChampionKillEvent>().Where(x => x.TimestampMs <= cutoffMs))
        {
            if (kill.KillerParticipantId == observation.ParticipantId && _participantsById.ContainsKey(observation.ParticipantId))
            {
                kills++;
                contributions.Add(new(kill.Source, kill.TimestampMs, "kill"));
            }

            if (kill.VictimParticipantId == observation.ParticipantId)
            {
                deaths++;
                contributions.Add(new(kill.Source, kill.TimestampMs, "death"));
            }

            if (kill.AssistingParticipantIds.Distinct().Contains(observation.ParticipantId))
            {
                assists++;
                contributions.Add(new(kill.Source, kill.TimestampMs, "assist"));
            }
        }

        return new(observation, kills, deaths, assists, contributions);
    }

    private static PlayerDelta Delta(PlayerState start, PlayerState end) => new(
        start.Observation.Position,
        end.Observation.Position,
        end.Observation.TotalGold - start.Observation.TotalGold,
        end.Observation.CurrentGold - start.Observation.CurrentGold,
        end.Observation.Xp - start.Observation.Xp,
        end.Observation.Level - start.Observation.Level,
        end.Observation.JungleCs - start.Observation.JungleCs,
        end.Observation.LaneCs - start.Observation.LaneCs,
        Difference(start.Observation.CurrentHealth, end.Observation.CurrentHealth),
        Difference(start.Observation.MaxHealth, end.Observation.MaxHealth),
        Difference(start.Observation.CurrentResource, end.Observation.CurrentResource),
        Difference(start.Observation.MaxResource, end.Observation.MaxResource),
        end.Kills - start.Kills,
        end.Deaths - start.Deaths,
        end.Assists - start.Assists);

    private static long? Difference(long? start, long? end) => start.HasValue && end.HasValue ? end - start : null;

    private void EnsureInRange(long timestampMs)
    {
        if (timestampMs < AvailableFromMs || timestampMs > AvailableToMs)
            throw new ReconstructionQueryException("timestamp_out_of_range", "Timestamp is outside recorded timeline coverage.", AvailableFromMs, AvailableToMs);
    }
}
