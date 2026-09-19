using Pathwise.Application.Reconstruction;
using Pathwise.Domain.Reconstruction;

namespace Pathwise.Api;

public sealed record ReconstructionContextDto(
    string MatchId,
    int ReconstructionVersion,
    string Patch,
    int QueueId,
    int MapId,
    int ReportedDurationSeconds,
    ReconstructionSourceDto Source);

public sealed record ReconstructionSourceDto(
    DateTimeOffset MatchRetrievedAtUtc,
    DateTimeOffset TimelineRetrievedAtUtc,
    string MatchEndpointVersion,
    string TimelineEndpointVersion);

public sealed record ReconstructionMetadataResponse(
    ReconstructionContextDto Context,
    IReadOnlyList<ParticipantDto> Participants,
    int ConfiguredParticipantId,
    EnemyResolutionDto EnemyResolution,
    CoverageDto Coverage,
    IReadOnlyList<string> SupportedEventKinds,
    IReadOnlyList<SourceDataIssueDto> SourceDataIssues);

public sealed record ReconstructionStateResponse(
    ReconstructionContextDto Context,
    IReadOnlyList<SourceDataIssueDto> SourceDataIssues,
    GameStateDto State);

public sealed record ReconstructionChangesResponse(
    ReconstructionContextDto Context,
    IReadOnlyList<SourceDataIssueDto> SourceDataIssues,
    GameChangesDto Changes);

public sealed record ParticipantDto(
    int ParticipantId,
    int TeamId,
    int ChampionId,
    string ChampionName,
    string? TeamPosition,
    bool Won,
    FinalKdaDto FinalKda);

public sealed record FinalKdaDto(int Kills, int Deaths, int Assists);
public sealed record EnemyResolutionDto(string Status, int? ParticipantId);
public sealed record CoverageDto(long AvailableFromMs, long AvailableToMs, IReadOnlyList<FrameTimestampDto> Frames);
public sealed record FrameTimestampDto(int FrameIndex, long TimestampMs);
public sealed record SourceDataIssueDto(string Code, string SourceReference, string Explanation, string HandlingOutcome);
public sealed record PositionDto(int X, int Y);
public sealed record SourceEventReferenceDto(int FrameIndex, int EventIndex);
public sealed record CombatContributionDto(SourceEventReferenceDto Source, long TimestampMs, string Contribution);

public sealed record PlayerObservationDto(
    int ParticipantId,
    PositionDto? Position,
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

public sealed record PlayerStateDto(
    PlayerObservationDto Observation,
    int Kills,
    int Deaths,
    int Assists,
    IReadOnlyList<CombatContributionDto> CombatEventReferences);

public sealed record GameStateDto(
    long RequestedTimestampMs,
    int SelectedFrameIndex,
    long SelectedFrameTimestampMs,
    long EventCutoffTimestampMs,
    PlayerStateDto ConfiguredPlayer,
    PlayerStateDto? EnemyJungler,
    EnemyResolutionDto EnemyResolution);

public sealed record PlayerDeltaDto(
    PositionDto? PositionBefore,
    PositionDto? PositionAfter,
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

public sealed record GameChangesDto(
    long RequestedStartTimestampMs,
    long RequestedEndTimestampMs,
    GameStateDto StartState,
    GameStateDto EndState,
    PlayerDeltaDto ConfiguredPlayerDelta,
    PlayerDeltaDto? EnemyJunglerDelta,
    IReadOnlyList<ReconstructionEventDto> Events);

public sealed record ReconstructionEventDto(
    long TimestampMs,
    string Kind,
    string SourceType,
    SourceEventReferenceDto Source,
    object Data);

public sealed record TeamAttributionDto(string Kind, int? SuppliedTeamId, int? ResolvedTeamId, string? DiagnosticReason);
public sealed record ChampionKillDataDto(int? KillerParticipantId, int VictimParticipantId, IReadOnlyList<int> AssistingParticipantIds, PositionDto? Position, int? Bounty, int? ShutdownBounty);
public sealed record EliteMonsterKillDataDto(string? MonsterType, string? MonsterSubType, int? KillerParticipantId, TeamAttributionDto TeamAttribution, IReadOnlyList<int> AssistingParticipantIds, PositionDto? Position, int? Bounty);
public sealed record StructureKillDataDto(string? StructureType, string? TowerType, string? Lane, TeamAttributionDto OwningTeam, int? KillerParticipantId, TeamAttributionDto KillerTeamAttribution, IReadOnlyList<int> AssistingParticipantIds, PositionDto? Position, int? Bounty, bool IsPlate);
public sealed record ItemTransactionDataDto(int? ParticipantId, string Action, int? ItemId, int? BeforeItemId, int? AfterItemId, int? GoldGain);
public sealed record WardActionDataDto(int? ParticipantId, string Action, string? WardType);
public sealed record LevelUpDataDto(int? ParticipantId, int? NewLevel);
public sealed record DragonSoulMarkerDataDto(string? Name, TeamAttributionDto TeamAttribution);
public sealed record ObjectiveBountyMarkerDataDto(TeamAttributionDto TeamAttribution, long? ScheduledStartTimestampMs);
public sealed record GameEndDataDto(TeamAttributionDto WinningTeam);

public static class ReconstructionApiMapper
{
    private static readonly string[] SupportedKinds =
    [
        "championKill", "eliteMonsterKill", "structureKill", "turretPlateDestroyed",
        "itemTransaction", "wardAction", "levelUp", "dragonSoulMarker",
        "objectiveBountyMarker", "gameEnd"
    ];

    public static ReconstructionMetadataResponse Metadata(ReconstructionResult<GameReconstruction> result) => new(
        Context(result.Reconstruction, result.Source),
        result.Reconstruction.Participants.Select(Participant).ToArray(),
        result.Reconstruction.ConfiguredParticipantId,
        Enemy(result.Reconstruction.EnemyResolution),
        new(
            result.Reconstruction.AvailableFromMs,
            result.Reconstruction.AvailableToMs,
            result.Reconstruction.Observations.Select(x => new FrameTimestampDto(x.FrameIndex, x.TimestampMs)).ToArray()),
        SupportedKinds,
        result.Reconstruction.SourceDataIssues.Select(Issue).ToArray());

    public static ReconstructionStateResponse State(ReconstructionResult<GameState> result) => new(
        Context(result.Reconstruction, result.Source),
        result.Reconstruction.SourceDataIssues.Select(Issue).ToArray(),
        State(result.Value));

    public static ReconstructionChangesResponse Changes(ReconstructionResult<GameChanges> result) => new(
        Context(result.Reconstruction, result.Source),
        result.Reconstruction.SourceDataIssues.Select(Issue).ToArray(),
        new(
            result.Value.RequestedStartTimestampMs,
            result.Value.RequestedEndTimestampMs,
            State(result.Value.StartState),
            State(result.Value.EndState),
            Delta(result.Value.ConfiguredPlayerDelta),
            result.Value.EnemyJunglerDelta is null ? null : Delta(result.Value.EnemyJunglerDelta),
            result.Value.Events.Select(Event).ToArray()));

    private static ReconstructionContextDto Context(GameReconstruction reconstruction, ReconstructionSourceMetadata source) => new(
        reconstruction.MatchId,
        reconstruction.ReconstructionVersion,
        reconstruction.Patch,
        reconstruction.QueueId,
        reconstruction.MapId,
        reconstruction.ReportedDurationSeconds,
        new(source.MatchRetrievedAtUtc, source.TimelineRetrievedAtUtc, source.MatchEndpointVersion, source.TimelineEndpointVersion));

    private static ParticipantDto Participant(Participant value) => new(
        value.ParticipantId, value.TeamId, value.ChampionId, value.ChampionName, value.TeamPosition, value.Won,
        new(value.FinalKills, value.FinalDeaths, value.FinalAssists));

    private static GameStateDto State(GameState value) => new(
        value.RequestedTimestampMs,
        value.SelectedFrameIndex,
        value.SelectedFrameTimestampMs,
        value.EventCutoffTimestampMs,
        PlayerState(value.ConfiguredPlayer),
        value.EnemyJungler is null ? null : PlayerState(value.EnemyJungler),
        Enemy(value.EnemyResolution));

    private static PlayerStateDto PlayerState(PlayerState value) => new(
        Observation(value.Observation),
        value.Kills,
        value.Deaths,
        value.Assists,
        value.CombatEventReferences.Select(x => new CombatContributionDto(Source(x.Source), x.TimestampMs, x.Contribution)).ToArray());

    private static PlayerObservationDto Observation(PlayerObservation value) => new(
        value.ParticipantId,
        Position(value.Position),
        value.TotalGold,
        value.CurrentGold,
        value.Xp,
        value.Level,
        value.JungleCs,
        value.LaneCs,
        value.CurrentHealth,
        value.MaxHealth,
        value.CurrentResource,
        value.MaxResource);

    private static PlayerDeltaDto Delta(PlayerDelta value) => new(
        Position(value.PositionBefore), Position(value.PositionAfter), value.TotalGold, value.CurrentGold, value.Xp,
        value.Level, value.JungleCs, value.LaneCs, value.CurrentHealth, value.MaxHealth,
        value.CurrentResource, value.MaxResource, value.Kills, value.Deaths, value.Assists);

    private static ReconstructionEventDto Event(ReconstructionEvent value)
    {
        object data = value switch
        {
            ChampionKillEvent x => new ChampionKillDataDto(x.KillerParticipantId, x.VictimParticipantId, x.AssistingParticipantIds, Position(x.Position), x.Bounty, x.ShutdownBounty),
            EliteMonsterKillEvent x => new EliteMonsterKillDataDto(x.MonsterType, x.MonsterSubType, x.KillerParticipantId, Team(x.TeamAttribution), x.AssistingParticipantIds, Position(x.Position), x.Bounty),
            StructureKillEvent x => new StructureKillDataDto(x.StructureType, x.TowerType, x.Lane, Team(x.OwningTeam), x.KillerParticipantId, Team(x.KillerTeamAttribution), x.AssistingParticipantIds, Position(x.Position), x.Bounty, x.IsPlate),
            ItemTransactionEvent x => new ItemTransactionDataDto(x.ParticipantId, x.Action, x.ItemId, x.BeforeItemId, x.AfterItemId, x.GoldGain),
            WardActionEvent x => new WardActionDataDto(x.ParticipantId, x.Action, x.WardType),
            LevelUpEvent x => new LevelUpDataDto(x.ParticipantId, x.NewLevel),
            DragonSoulMarkerEvent x => new DragonSoulMarkerDataDto(x.Name, Team(x.TeamAttribution)),
            ObjectiveBountyMarkerEvent x => new ObjectiveBountyMarkerDataDto(Team(x.TeamAttribution), x.ScheduledStartTimestampMs),
            GameEndEvent x => new GameEndDataDto(Team(x.WinningTeam)),
            _ => throw new InvalidOperationException($"Unsupported reconstruction event {value.GetType().Name}.")
        };
        return new(value.TimestampMs, value.Kind, value.SourceType, Source(value.Source), data);
    }

    private static SourceDataIssueDto Issue(SourceDataIssue value) => new(value.Code, value.SourceReference, value.Explanation, value.HandlingOutcome);
    private static EnemyResolutionDto Enemy(EnemyJunglerResolution value) => new(value.Status.ToString().ToLowerInvariant(), value.ParticipantId);
    private static TeamAttributionDto Team(TeamAttribution value) => new(value.Kind.ToString(), value.SuppliedTeamId, value.ResolvedTeamId, value.DiagnosticReason);
    private static SourceEventReferenceDto Source(SourceEventReference value) => new(value.FrameIndex, value.EventIndex);
    private static PositionDto? Position(Position? value) => value is null ? null : new(value.X, value.Y);
}
