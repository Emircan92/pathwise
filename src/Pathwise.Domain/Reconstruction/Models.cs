namespace Pathwise.Domain.Reconstruction;

public enum EnemyResolutionStatus { Resolved, Missing, Ambiguous }
public enum TeamAttributionKind { KnownTeam, Neutral, Unknown }

public sealed record SourceEventReference(int FrameIndex, int EventIndex);
public sealed record SourceDataIssue(string Code, string SourceReference, string Explanation, string HandlingOutcome);
public sealed record Position(int X, int Y);

public sealed record Participant(
    int ParticipantId,
    int TeamId,
    int ChampionId,
    string ChampionName,
    string? TeamPosition,
    bool Won,
    int FinalKills,
    int FinalDeaths,
    int FinalAssists);

public sealed record EnemyJunglerResolution(EnemyResolutionStatus Status, int? ParticipantId);

public sealed record PlayerObservation(
    int ParticipantId,
    Position? Position,
    long TotalGold,
    long CurrentGold,
    long Xp,
    int Level,
    long JungleCs,
    long LaneCs,
    long? CurrentHealth,
    long? MaxHealth,
    long? CurrentResource,
    long? MaxResource);

public sealed record FrameObservation(
    int FrameIndex,
    long TimestampMs,
    PlayerObservation ConfiguredPlayer,
    PlayerObservation? EnemyJungler);

public sealed record TeamAttribution(
    TeamAttributionKind Kind,
    int? SuppliedTeamId,
    int? ResolvedTeamId,
    string? DiagnosticReason);

public abstract record ReconstructionEvent(
    long TimestampMs,
    string Kind,
    string SourceType,
    SourceEventReference Source);

public sealed record ChampionKillEvent(
    long TimestampMs,
    SourceEventReference Source,
    int? KillerParticipantId,
    int VictimParticipantId,
    IReadOnlyList<int> AssistingParticipantIds,
    Position? Position,
    int? Bounty,
    int? ShutdownBounty)
    : ReconstructionEvent(TimestampMs, "championKill", "CHAMPION_KILL", Source);

public sealed record EliteMonsterKillEvent(
    long TimestampMs,
    SourceEventReference Source,
    string? MonsterType,
    string? MonsterSubType,
    int? KillerParticipantId,
    TeamAttribution TeamAttribution,
    IReadOnlyList<int> AssistingParticipantIds,
    Position? Position,
    int? Bounty)
    : ReconstructionEvent(TimestampMs, "eliteMonsterKill", "ELITE_MONSTER_KILL", Source);

public sealed record StructureKillEvent(
    long TimestampMs,
    SourceEventReference Source,
    string? StructureType,
    string? TowerType,
    string? Lane,
    TeamAttribution OwningTeam,
    int? KillerParticipantId,
    TeamAttribution KillerTeamAttribution,
    IReadOnlyList<int> AssistingParticipantIds,
    Position? Position,
    int? Bounty,
    bool IsPlate)
    : ReconstructionEvent(TimestampMs, IsPlate ? "turretPlateDestroyed" : "structureKill", IsPlate ? "TURRET_PLATE_DESTROYED" : "BUILDING_KILL", Source);

public sealed record ItemTransactionEvent(
    long TimestampMs,
    SourceEventReference Source,
    int? ParticipantId,
    string Action,
    int? ItemId,
    int? BeforeItemId,
    int? AfterItemId,
    int? GoldGain)
    : ReconstructionEvent(TimestampMs, "itemTransaction", SourceTypeFor(Action), Source)
{
    private static string SourceTypeFor(string action) => action switch
    {
        "purchased" => "ITEM_PURCHASED",
        "sold" => "ITEM_SOLD",
        "destroyed" => "ITEM_DESTROYED",
        "undo" => "ITEM_UNDO",
        _ => "ITEM_TRANSACTION"
    };
}

public sealed record WardActionEvent(
    long TimestampMs,
    SourceEventReference Source,
    int? ParticipantId,
    string Action,
    string? WardType)
    : ReconstructionEvent(TimestampMs, "wardAction", Action == "placed" ? "WARD_PLACED" : "WARD_KILL", Source);

public sealed record LevelUpEvent(
    long TimestampMs,
    SourceEventReference Source,
    int? ParticipantId,
    int? NewLevel)
    : ReconstructionEvent(TimestampMs, "levelUp", "LEVEL_UP", Source);

public sealed record DragonSoulMarkerEvent(
    long TimestampMs,
    SourceEventReference Source,
    string? Name,
    TeamAttribution TeamAttribution)
    : ReconstructionEvent(TimestampMs, "dragonSoulMarker", "DRAGON_SOUL_GIVEN", Source);

public sealed record ObjectiveBountyMarkerEvent(
    long TimestampMs,
    SourceEventReference Source,
    TeamAttribution TeamAttribution,
    long? ScheduledStartTimestampMs)
    : ReconstructionEvent(TimestampMs, "objectiveBountyMarker", "OBJECTIVE_BOUNTY_PRESTART", Source);

public sealed record GameEndEvent(
    long TimestampMs,
    SourceEventReference Source,
    TeamAttribution WinningTeam)
    : ReconstructionEvent(TimestampMs, "gameEnd", "GAME_END", Source);

public sealed record CombatEventContribution(SourceEventReference Source, long TimestampMs, string Contribution);

public sealed record PlayerState(
    PlayerObservation Observation,
    int Kills,
    int Deaths,
    int Assists,
    IReadOnlyList<CombatEventContribution> CombatEventReferences);

public sealed record GameState(
    long RequestedTimestampMs,
    int SelectedFrameIndex,
    long SelectedFrameTimestampMs,
    long EventCutoffTimestampMs,
    PlayerState ConfiguredPlayer,
    PlayerState? EnemyJungler,
    EnemyJunglerResolution EnemyResolution);

public sealed record PlayerDelta(
    Position? PositionBefore,
    Position? PositionAfter,
    long TotalGold,
    long CurrentGold,
    long Xp,
    int Level,
    long JungleCs,
    long LaneCs,
    long? CurrentHealth,
    long? MaxHealth,
    long? CurrentResource,
    long? MaxResource,
    int Kills,
    int Deaths,
    int Assists);

public sealed record GameChanges(
    long RequestedStartTimestampMs,
    long RequestedEndTimestampMs,
    GameState StartState,
    GameState EndState,
    PlayerDelta ConfiguredPlayerDelta,
    PlayerDelta? EnemyJunglerDelta,
    IReadOnlyList<ReconstructionEvent> Events);

public sealed record ReconstructionInput(
    string MatchId,
    string Patch,
    int QueueId,
    int MapId,
    int ReportedDurationSeconds,
    IReadOnlyList<Participant> Participants,
    int ConfiguredParticipantId,
    EnemyJunglerResolution EnemyResolution,
    IReadOnlyList<FrameObservation> Observations,
    IReadOnlyList<ReconstructionEvent> Events,
    IReadOnlyList<SourceDataIssue> SourceDataIssues);

public sealed class ReconstructionQueryException(
    string code,
    string message,
    long availableFromMs,
    long availableToMs) : ArgumentOutOfRangeException(null, message)
{
    public string Code { get; } = code;
    public long AvailableFromMs { get; } = availableFromMs;
    public long AvailableToMs { get; } = availableToMs;
}
