using System.Diagnostics;
using System.Text.Json;
using Pathwise.Application.Reconstruction;
using Pathwise.Domain.Reconstruction;

namespace Pathwise.Infrastructure.Reconstruction;

public sealed class RiotReconstructionMapper
{
    private static readonly HashSet<string> DeferredEventTypes =
        ["CHAMPION_SPECIAL_KILL", "SKILL_LEVEL_UP", "PAUSE_END"];

    public ReconstructionInput Map(
        string matchJson,
        string timelineJson,
        string expectedMatchId,
        string configuredPlayerPuuid)
    {
        try
        {
            using var matchDocument = JsonDocument.Parse(matchJson);
            using var timelineDocument = JsonDocument.Parse(timelineJson);
            return MapDocuments(matchDocument.RootElement, timelineDocument.RootElement, expectedMatchId, configuredPlayerPuuid);
        }
        catch (JsonException ex)
        {
            throw new ReconstructionMappingException("malformed_json", "A stored reconstruction payload contains malformed JSON.", ex);
        }
    }

    private static ReconstructionInput MapDocuments(
        JsonElement matchRoot,
        JsonElement timelineRoot,
        string expectedMatchId,
        string configuredPlayerPuuid)
    {
        var matchMetadata = RequiredObject(matchRoot, "metadata", "match_metadata_missing");
        var timelineMetadata = RequiredObject(timelineRoot, "metadata", "timeline_metadata_missing");
        ValidateMatchId(matchMetadata, expectedMatchId, "match");
        ValidateMatchId(timelineMetadata, expectedMatchId, "timeline");

        var matchInfo = RequiredObject(matchRoot, "info", "match_info_missing");
        var timelineInfo = RequiredObject(timelineRoot, "info", "timeline_info_missing");
        var queueId = RequiredInt(matchInfo, "queueId", "queue_id_missing");
        if (queueId != 420)
            throw Failure("unsupported_queue", "Only Ranked Solo queue 420 is supported for reconstruction.");

        var mapId = RequiredInt(matchInfo, "mapId", "map_id_missing");
        var durationSeconds = RequiredInt(matchInfo, "gameDuration", "game_duration_missing");
        var patch = RequiredString(matchInfo, "gameVersion", "game_version_missing");
        var participantsElement = RequiredArray(matchInfo, "participants", "participants_missing");
        var participants = new List<Participant>();
        var puuidToParticipantId = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var element in participantsElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
                throw Failure("participant_invalid", "A match participant record is not an object.");

            var participantId = RequiredInt(element, "participantId", "participant_id_missing");
            var puuid = RequiredString(element, "puuid", "participant_puuid_missing");
            if (participants.Any(x => x.ParticipantId == participantId))
                throw Failure("duplicate_participant_id", $"Participant ID {participantId} is duplicated.");
            if (!puuidToParticipantId.TryAdd(puuid, participantId))
                throw Failure("duplicate_participant_linkage", "Participant PUUID linkage is duplicated.");

            participants.Add(new(
                participantId,
                RequiredInt(element, "teamId", "participant_team_missing"),
                RequiredInt(element, "championId", "participant_champion_missing"),
                RequiredString(element, "championName", "participant_champion_missing"),
                OptionalString(element, "teamPosition"),
                RequiredBool(element, "win", "participant_result_missing"),
                RequiredInt(element, "kills", "participant_final_kda_missing"),
                RequiredInt(element, "deaths", "participant_final_kda_missing"),
                RequiredInt(element, "assists", "participant_final_kda_missing")));
        }

        if (!puuidToParticipantId.TryGetValue(configuredPlayerPuuid, out var configuredParticipantId))
            throw Failure("configured_participant_missing", "The configured account is not linked to a match participant.");

        var configuredParticipant = participants.Single(x => x.ParticipantId == configuredParticipantId);
        if (!string.Equals(configuredParticipant.TeamPosition, "JUNGLE", StringComparison.OrdinalIgnoreCase))
            throw Failure("unsupported_role", "The configured participant is not recorded as JUNGLE.");

        ValidateMetadataParticipants(matchMetadata, timelineMetadata, puuidToParticipantId.Keys);
        ValidateTimelineParticipants(timelineInfo, puuidToParticipantId);

        var enemies = participants.Where(x => x.TeamId != configuredParticipant.TeamId && string.Equals(x.TeamPosition, "JUNGLE", StringComparison.OrdinalIgnoreCase)).ToArray();
        var enemyResolution = enemies.Length switch
        {
            1 => new EnemyJunglerResolution(EnemyResolutionStatus.Resolved, enemies[0].ParticipantId),
            0 => new EnemyJunglerResolution(EnemyResolutionStatus.Missing, null),
            _ => new EnemyJunglerResolution(EnemyResolutionStatus.Ambiguous, null)
        };

        var issues = new List<SourceDataIssue>();
        if (enemyResolution.Status != EnemyResolutionStatus.Resolved)
            issues.Add(new("enemy_jungler_unresolved", "match.info.participants", $"Opposing JUNGLE role resolution is {enemyResolution.Status.ToString().ToLowerInvariant()}.", "enemy omitted"));

        var framesElement = RequiredArray(timelineInfo, "frames", "frames_missing");
        if (framesElement.GetArrayLength() == 0)
            throw Failure("frames_missing", "The timeline contains no frames.");

        var frames = framesElement.EnumerateArray().ToArray();
        var observations = new List<FrameObservation>(frames.Length);
        long previousTimestamp = -1;
        PlayerObservation? previousConfigured = null;
        PlayerObservation? previousEnemy = null;
        for (var frameIndex = 0; frameIndex < frames.Length; frameIndex++)
        {
            var frame = frames[frameIndex];
            if (frame.ValueKind != JsonValueKind.Object)
                throw Failure("frame_invalid", $"Timeline frame {frameIndex} is not an object.");
            var timestamp = RequiredLong(frame, "timestamp", "frame_timestamp_missing");
            if (timestamp < 0 || timestamp <= previousTimestamp)
                throw Failure("frame_timestamps_not_increasing", $"Timeline frame {frameIndex} does not have a strictly increasing non-negative timestamp.");
            previousTimestamp = timestamp;

            var participantFrames = RequiredObject(frame, "participantFrames", "participant_frames_missing");
            var configured = MapObservation(participantFrames, configuredParticipantId, frameIndex, issues);
            PlayerObservation? enemy = null;
            if (enemyResolution.ParticipantId is { } enemyId)
                enemy = MapObservation(participantFrames, enemyId, frameIndex, issues);

            AddCounterRegressionIssues(previousConfigured, configured, frameIndex, issues);
            AddCounterRegressionIssues(previousEnemy, enemy, frameIndex, issues);
            observations.Add(new(frameIndex, timestamp, configured, enemy));
            previousConfigured = configured;
            previousEnemy = enemy;
        }

        var events = MapEvents(frames, observations[0].TimestampMs, observations[^1].TimestampMs, participants, issues);
        AddFinalKdaIssues(participants, events, issues);

        return new(
            expectedMatchId,
            patch,
            queueId,
            mapId,
            durationSeconds,
            participants,
            configuredParticipantId,
            enemyResolution,
            observations,
            events,
            issues);
    }

    private static PlayerObservation MapObservation(
        JsonElement participantFrames,
        int participantId,
        int frameIndex,
        List<SourceDataIssue> issues)
    {
        if (!participantFrames.TryGetProperty(participantId.ToString(), out var frame) || frame.ValueKind != JsonValueKind.Object)
            throw Failure("required_observation_missing", $"Frame {frameIndex} is missing required participant {participantId} observation.");

        var source = $"frame[{frameIndex}].participantFrames[{participantId}]";
        var recordedId = RequiredInt(frame, "participantId", "observation_participant_missing");
        if (recordedId != participantId)
            throw Failure("participant_linkage_mismatch", $"{source} contains participant ID {recordedId}.");

        var position = OptionalPosition(frame, "position", source, issues);
        var championStats = frame.TryGetProperty("championStats", out var stats) && stats.ValueKind == JsonValueKind.Object ? stats : default;
        if (championStats.ValueKind != JsonValueKind.Object)
            issues.Add(new("optional_champion_stats_missing", source, "Champion health and resource observation is unavailable.", "nullable fields retained"));

        return new(
            participantId,
            position,
            RequiredLong(frame, "totalGold", "required_observation_missing"),
            RequiredLong(frame, "currentGold", "required_observation_missing"),
            RequiredLong(frame, "xp", "required_observation_missing"),
            RequiredInt(frame, "level", "required_observation_missing"),
            RequiredLong(frame, "jungleMinionsKilled", "required_observation_missing"),
            RequiredLong(frame, "minionsKilled", "required_observation_missing"),
            OptionalLong(championStats, "health", source, issues),
            OptionalLong(championStats, "healthMax", source, issues),
            OptionalLong(championStats, "power", source, issues),
            OptionalLong(championStats, "powerMax", source, issues));
    }

    private static IReadOnlyList<ReconstructionEvent> MapEvents(
        JsonElement[] frames,
        long coverageStart,
        long coverageEnd,
        IReadOnlyList<Participant> participants,
        List<SourceDataIssue> issues)
    {
        var result = new List<ReconstructionEvent>();
        var participantIds = participants.Select(x => x.ParticipantId).ToHashSet();
        var teamIds = participants.Select(x => x.TeamId).ToHashSet();
        var unknownTypes = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var frameIndex = 0; frameIndex < frames.Length; frameIndex++)
        {
            var events = RequiredArray(frames[frameIndex], "events", "frame_events_missing").EnumerateArray().ToArray();
            for (var eventIndex = 0; eventIndex < events.Length; eventIndex++)
            {
                var source = new SourceEventReference(frameIndex, eventIndex);
                var sourceText = SourceText(source);
                var element = events[eventIndex];
                if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(typeElement.GetString()))
                    throw Failure("event_type_missing", $"{sourceText} has no trustworthy event type.");

                var type = typeElement.GetString()!;
                if (DeferredEventTypes.Contains(type)) continue;
                if (!IsSupported(type))
                {
                    unknownTypes[type] = unknownTypes.GetValueOrDefault(type) + 1;
                    continue;
                }

                var critical = type == "CHAMPION_KILL";
                if (!TryEventTimestamp(element, out var timestamp))
                {
                    if (critical) throw Failure("champion_kill_timestamp_invalid", $"{sourceText} has an invalid champion-kill timestamp.");
                    issues.Add(new("event_timestamp_invalid", sourceText, $"{type} has no reliable timestamp.", "event omitted"));
                    continue;
                }

                if (timestamp < coverageStart || timestamp > coverageEnd)
                {
                    if (critical) throw Failure("champion_kill_outside_coverage", $"{sourceText} is outside recorded frame coverage.");
                    issues.Add(new("event_outside_coverage", sourceText, $"{type} at {timestamp} is outside recorded frame coverage.", "event omitted"));
                    continue;
                }

                if (critical)
                    result.Add(MapChampionKill(element, timestamp, source, participantIds, issues));
                else
                {
                    var mapped = MapNonCriticalEvent(element, type, timestamp, source, participants, teamIds, issues);
                    if (mapped is not null) result.Add(mapped);
                }
            }
        }

        foreach (var (type, count) in unknownTypes.OrderBy(x => x.Key))
            issues.Add(new("unsupported_event_type", "timeline.info.frames", $"Unsupported event type {type} occurred {count} time(s).", "events omitted"));

        return result;
    }

    private static ChampionKillEvent MapChampionKill(
        JsonElement element,
        long timestamp,
        SourceEventReference source,
        HashSet<int> participantIds,
        List<SourceDataIssue> issues)
    {
        var sourceText = SourceText(source);
        if (!TryInt(element, "killerId", out var killerId))
            throw Failure("champion_kill_killer_invalid", $"{sourceText} has an invalid killer reference.");
        if (!TryInt(element, "victimId", out var victimId) || !participantIds.Contains(victimId))
            throw Failure("champion_kill_victim_invalid", $"{sourceText} has an invalid victim reference.");

        var assists = Array.Empty<int>();
        if (element.TryGetProperty("assistingParticipantIds", out var assistsElement))
        {
            if (assistsElement.ValueKind != JsonValueKind.Array)
                throw Failure("champion_kill_assists_invalid", $"{sourceText} has malformed assist data.");
            var reported = new List<int>();
            foreach (var assist in assistsElement.EnumerateArray())
            {
                if (!assist.TryGetInt32(out var id) || !participantIds.Contains(id))
                    throw Failure("champion_kill_assists_invalid", $"{sourceText} has malformed assist data.");
                reported.Add(id);
            }
            assists = reported.ToArray();
        }

        var position = OptionalEventPosition(element, sourceText, issues);
        var bounty = OptionalEventInt(element, "bounty", sourceText, issues);
        var shutdownBounty = OptionalEventInt(element, "shutdownBounty", sourceText, issues);
        return new(timestamp, source, killerId, victimId, assists, position, bounty, shutdownBounty);
    }

    private static ReconstructionEvent? MapNonCriticalEvent(
        JsonElement element,
        string type,
        long timestamp,
        SourceEventReference source,
        IReadOnlyList<Participant> participants,
        HashSet<int> teamIds,
        List<SourceDataIssue> issues)
    {
        var sourceText = SourceText(source);
        if (type == "ELITE_MONSTER_KILL" && string.IsNullOrWhiteSpace(OptionalString(element, "monsterType")))
            return OmitUnrepresentable(type, sourceText, "monsterType is absent or invalid.", issues);
        if (type is "ITEM_PURCHASED" or "ITEM_SOLD" or "ITEM_DESTROYED" &&
            (!TryInt(element, "participantId", out _) || !TryInt(element, "itemId", out _)))
            return OmitUnrepresentable(type, sourceText, "participantId or itemId is absent or invalid.", issues);
        if (type == "ITEM_UNDO" &&
            (!TryInt(element, "participantId", out _) || !TryInt(element, "beforeId", out _) || !TryInt(element, "afterId", out _)))
            return OmitUnrepresentable(type, sourceText, "participantId, beforeId, or afterId is absent or invalid.", issues);
        if (type == "WARD_PLACED" && !TryInt(element, "creatorId", out _))
            return OmitUnrepresentable(type, sourceText, "creatorId is absent or invalid.", issues);
        if (type == "WARD_KILL" && !TryInt(element, "killerId", out _))
            return OmitUnrepresentable(type, sourceText, "killerId is absent or invalid.", issues);
        if (type == "LEVEL_UP" && (!TryInt(element, "participantId", out _) || !TryInt(element, "level", out _)))
            return OmitUnrepresentable(type, sourceText, "participantId or level is absent or invalid.", issues);

        return type switch
        {
            "ELITE_MONSTER_KILL" => new EliteMonsterKillEvent(
                timestamp,
                source,
                OptionalEventString(element, "monsterType", sourceText, issues),
                OptionalEventString(element, "monsterSubType", sourceText, issues),
                OptionalEventInt(element, "killerId", sourceText, issues),
                ResolveSuppliedTeam(element, "killerTeamId", participants, teamIds, sourceText, issues, checkKillerConflict: true),
                OptionalParticipantArray(element, "assistingParticipantIds", sourceText, issues),
                OptionalEventPosition(element, sourceText, issues),
                OptionalEventInt(element, "bounty", sourceText, issues)),

            "BUILDING_KILL" => MapStructure(element, timestamp, source, participants, teamIds, issues, false),
            "TURRET_PLATE_DESTROYED" => MapStructure(element, timestamp, source, participants, teamIds, issues, true),
            "ITEM_PURCHASED" => MapItem(element, timestamp, source, "purchased", issues),
            "ITEM_SOLD" => MapItem(element, timestamp, source, "sold", issues),
            "ITEM_DESTROYED" => MapItem(element, timestamp, source, "destroyed", issues),
            "ITEM_UNDO" => MapItem(element, timestamp, source, "undo", issues),
            "WARD_PLACED" => new WardActionEvent(timestamp, source, OptionalEventInt(element, "creatorId", sourceText, issues), "placed", OptionalEventString(element, "wardType", sourceText, issues)),
            "WARD_KILL" => new WardActionEvent(timestamp, source, OptionalEventInt(element, "killerId", sourceText, issues), "killed", OptionalEventString(element, "wardType", sourceText, issues)),
            "LEVEL_UP" => new LevelUpEvent(timestamp, source, OptionalEventInt(element, "participantId", sourceText, issues), OptionalEventInt(element, "level", sourceText, issues)),
            "DRAGON_SOUL_GIVEN" => new DragonSoulMarkerEvent(timestamp, source, OptionalEventString(element, "name", sourceText, issues), ResolveSuppliedTeam(element, "teamId", participants, teamIds, sourceText, issues)),
            "OBJECTIVE_BOUNTY_PRESTART" => new ObjectiveBountyMarkerEvent(timestamp, source, ResolveSuppliedTeam(element, "teamId", participants, teamIds, sourceText, issues), OptionalEventLong(element, "actualStartTime", sourceText, issues)),
            "GAME_END" => new GameEndEvent(timestamp, source, ResolveSuppliedTeam(element, "winningTeam", participants, teamIds, sourceText, issues)),
            _ => throw new UnreachableException()
        };
    }

    private static ReconstructionEvent? OmitUnrepresentable(string type, string source, string reason, List<SourceDataIssue> issues)
    {
        issues.Add(new("event_identity_invalid", source, $"{type} cannot be represented because {reason}", "event omitted"));
        return null;
    }

    private static StructureKillEvent MapStructure(
        JsonElement element,
        long timestamp,
        SourceEventReference source,
        IReadOnlyList<Participant> participants,
        HashSet<int> teamIds,
        List<SourceDataIssue> issues,
        bool isPlate)
    {
        var sourceText = SourceText(source);
        var killerId = OptionalEventInt(element, "killerId", sourceText, issues);
        var killer = killerId is null ? null : participants.SingleOrDefault(x => x.ParticipantId == killerId);
        var killerTeam = killer is null
            ? new TeamAttribution(TeamAttributionKind.Unknown, null, null, "Killer is absent or is not a known participant.")
            : new TeamAttribution(TeamAttributionKind.KnownTeam, null, killer.TeamId, "Resolved from known killer participant.");
        return new(
            timestamp,
            source,
            isPlate ? "TOWER_BUILDING" : OptionalEventString(element, "buildingType", sourceText, issues),
            OptionalEventString(element, "towerType", sourceText, issues),
            OptionalEventString(element, "laneType", sourceText, issues),
            ResolveSuppliedTeam(element, "teamId", participants, teamIds, sourceText, issues),
            killerId,
            killerTeam,
            OptionalParticipantArray(element, "assistingParticipantIds", sourceText, issues),
            OptionalEventPosition(element, sourceText, issues),
            OptionalEventInt(element, "bounty", sourceText, issues),
            isPlate);
    }

    private static ItemTransactionEvent MapItem(JsonElement element, long timestamp, SourceEventReference source, string action, List<SourceDataIssue> issues)
    {
        var sourceText = SourceText(source);
        return new(
            timestamp,
            source,
            OptionalEventInt(element, "participantId", sourceText, issues),
            action,
            action == "undo" ? null : OptionalEventInt(element, "itemId", sourceText, issues),
            action == "undo" ? OptionalEventInt(element, "beforeId", sourceText, issues) : null,
            action == "undo" ? OptionalEventInt(element, "afterId", sourceText, issues) : null,
            action == "undo" ? OptionalEventInt(element, "goldGain", sourceText, issues) : null);
    }

    private static TeamAttribution ResolveSuppliedTeam(
        JsonElement element,
        string property,
        IReadOnlyList<Participant> participants,
        HashSet<int> teamIds,
        string source,
        List<SourceDataIssue> issues,
        bool checkKillerConflict = false)
    {
        if (!TryInt(element, property, out var supplied))
        {
            issues.Add(new("team_attribution_unknown", source, $"{property} is absent or invalid.", "unknown attribution retained"));
            return new(TeamAttributionKind.Unknown, null, null, $"{property} is absent or invalid.");
        }

        if (supplied == 300)
            return new(TeamAttributionKind.Neutral, supplied, null, null);
        if (supplied == 0)
        {
            issues.Add(new("team_attribution_unknown", source, $"{property} is zero.", "unknown attribution retained"));
            return new(TeamAttributionKind.Unknown, supplied, null, $"{property} is zero.");
        }
        if (!teamIds.Contains(supplied))
        {
            issues.Add(new("team_attribution_unknown", source, $"{property} contains unrecognized team {supplied}.", "unknown attribution retained"));
            return new(TeamAttributionKind.Unknown, supplied, null, $"{property} is unrecognized.");
        }

        if (checkKillerConflict && TryInt(element, "killerId", out var killerId))
        {
            var killer = participants.SingleOrDefault(x => x.ParticipantId == killerId);
            if (killer is not null && killer.TeamId != supplied)
            {
                issues.Add(new("team_attribution_conflict", source, $"Reported team {supplied} conflicts with killer participant {killerId} team {killer.TeamId}.", "unknown attribution retained"));
                return new(TeamAttributionKind.Unknown, supplied, null, "Supplied team conflicts with known killer team.");
            }
        }

        return new(TeamAttributionKind.KnownTeam, supplied, supplied, null);
    }

    private static void AddCounterRegressionIssues(PlayerObservation? previous, PlayerObservation? current, int frameIndex, List<SourceDataIssue> issues)
    {
        if (previous is null || current is null) return;
        var regressions = new List<string>();
        if (current.TotalGold < previous.TotalGold) regressions.Add("totalGold");
        if (current.Xp < previous.Xp) regressions.Add("xp");
        if (current.Level < previous.Level) regressions.Add("level");
        if (current.JungleCs < previous.JungleCs) regressions.Add("jungleCs");
        if (current.LaneCs < previous.LaneCs) regressions.Add("laneCs");
        if (regressions.Count > 0)
            issues.Add(new("observation_counter_regression", $"frame[{frameIndex}].participantFrames[{current.ParticipantId}]", $"Observed counters regressed: {string.Join(", ", regressions)}.", "source anomaly retained"));
    }

    private static void AddFinalKdaIssues(IReadOnlyList<Participant> participants, IReadOnlyList<ReconstructionEvent> events, List<SourceDataIssue> issues)
    {
        var kills = events.OfType<ChampionKillEvent>().ToArray();
        foreach (var participant in participants)
        {
            var reconstructedKills = kills.Count(x => x.KillerParticipantId == participant.ParticipantId);
            var reconstructedDeaths = kills.Count(x => x.VictimParticipantId == participant.ParticipantId);
            var reconstructedAssists = kills.Count(x => x.AssistingParticipantIds.Distinct().Contains(participant.ParticipantId));
            if (reconstructedKills != participant.FinalKills || reconstructedDeaths != participant.FinalDeaths || reconstructedAssists != participant.FinalAssists)
                issues.Add(new("final_kda_mismatch", $"match.info.participants[{participant.ParticipantId}]", $"Reconstructed K/D/A {reconstructedKills}/{reconstructedDeaths}/{reconstructedAssists} differs from reported {participant.FinalKills}/{participant.FinalDeaths}/{participant.FinalAssists}.", "source values retained"));
        }
    }

    private static void ValidateMatchId(JsonElement metadata, string expected, string source)
    {
        var actual = RequiredString(metadata, "matchId", "match_id_missing");
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw Failure("match_id_mismatch", $"Stored {source} payload match ID does not match {expected}.");
    }

    private static void ValidateMetadataParticipants(JsonElement matchMetadata, JsonElement timelineMetadata, IEnumerable<string> participantPuuids)
    {
        var expected = participantPuuids.ToArray();
        foreach (var (metadata, source) in new[] { (matchMetadata, "match"), (timelineMetadata, "timeline") })
        {
            var values = RequiredArray(metadata, "participants", "metadata_participants_missing").EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : null).ToArray();
            if (values.Any(string.IsNullOrWhiteSpace) || !values.SequenceEqual(expected))
                throw Failure("participant_linkage_mismatch", $"{source} metadata participant linkage does not match match participants.");
        }
    }

    private static void ValidateTimelineParticipants(JsonElement timelineInfo, IReadOnlyDictionary<string, int> puuidToParticipantId)
    {
        var timelineParticipants = RequiredArray(timelineInfo, "participants", "timeline_participants_missing").EnumerateArray().ToArray();
        if (timelineParticipants.Length != puuidToParticipantId.Count)
            throw Failure("participant_linkage_mismatch", "Timeline participant count does not match the match payload.");
        foreach (var participant in timelineParticipants)
        {
            var id = RequiredInt(participant, "participantId", "timeline_participant_invalid");
            var puuid = RequiredString(participant, "puuid", "timeline_participant_invalid");
            if (!puuidToParticipantId.TryGetValue(puuid, out var expectedId) || expectedId != id)
                throw Failure("participant_linkage_mismatch", "Timeline participant linkage does not match the match payload.");
        }
    }

    private static bool IsSupported(string type) => type is
        "CHAMPION_KILL" or "ELITE_MONSTER_KILL" or "BUILDING_KILL" or "TURRET_PLATE_DESTROYED" or
        "ITEM_PURCHASED" or "ITEM_SOLD" or "ITEM_DESTROYED" or "ITEM_UNDO" or
        "WARD_PLACED" or "WARD_KILL" or "LEVEL_UP" or "DRAGON_SOUL_GIVEN" or
        "OBJECTIVE_BOUNTY_PRESTART" or "GAME_END";

    private static JsonElement RequiredObject(JsonElement parent, string property, string code)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Object)
            throw Failure(code, $"Required object {property} is missing or invalid.");
        return value;
    }

    private static JsonElement RequiredArray(JsonElement parent, string property, string code)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
            throw Failure(code, $"Required array {property} is missing or invalid.");
        return value;
    }

    private static string RequiredString(JsonElement parent, string property, string code)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw Failure(code, $"Required string {property} is missing or invalid.");
        return value.GetString()!;
    }

    private static int RequiredInt(JsonElement parent, string property, string code)
    {
        if (!TryInt(parent, property, out var value)) throw Failure(code, $"Required integer {property} is missing or invalid.");
        return value;
    }

    private static long RequiredLong(JsonElement parent, string property, string code)
    {
        if (!TryLong(parent, property, out var value)) throw Failure(code, $"Required integer {property} is missing or invalid.");
        return value;
    }

    private static bool RequiredBool(JsonElement parent, string property, string code)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw Failure(code, $"Required boolean {property} is missing or invalid.");
        return value.GetBoolean();
    }

    private static string? OptionalString(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool TryInt(JsonElement parent, string property, out int value)
    {
        value = default;
        return parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value);
    }

    private static bool TryLong(JsonElement parent, string property, out long value)
    {
        value = default;
        return parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out value);
    }

    private static bool TryEventTimestamp(JsonElement element, out long timestamp) => TryLong(element, "timestamp", out timestamp) && timestamp >= 0;

    private static Position? OptionalPosition(JsonElement parent, string property, string source, List<SourceDataIssue> issues)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Object || !TryInt(value, "x", out var x) || !TryInt(value, "y", out var y))
        {
            issues.Add(new("optional_position_missing", source, "Position is absent or invalid.", "nullable position retained"));
            return null;
        }
        return new(x, y);
    }

    private static Position? OptionalEventPosition(JsonElement element, string source, List<SourceDataIssue> issues)
    {
        if (!element.TryGetProperty("position", out _)) return null;
        return OptionalPosition(element, "position", source, issues);
    }

    private static long? OptionalLong(JsonElement parent, string property, string source, List<SourceDataIssue> issues)
    {
        if (TryLong(parent, property, out var value)) return value;
        issues.Add(new("optional_observation_missing", source, $"{property} is absent or invalid.", "nullable value retained"));
        return null;
    }

    private static int? OptionalEventInt(JsonElement parent, string property, string source, List<SourceDataIssue> issues)
    {
        if (TryInt(parent, property, out var value)) return value;
        if (parent.TryGetProperty(property, out _))
            issues.Add(new("event_field_invalid", source, $"{property} is invalid.", "partial event retained"));
        return null;
    }

    private static long? OptionalEventLong(JsonElement parent, string property, string source, List<SourceDataIssue> issues)
    {
        if (TryLong(parent, property, out var value)) return value;
        if (parent.TryGetProperty(property, out _))
            issues.Add(new("event_field_invalid", source, $"{property} is invalid.", "partial event retained"));
        return null;
    }

    private static string? OptionalEventString(JsonElement parent, string property, string source, List<SourceDataIssue> issues)
    {
        if (!parent.TryGetProperty(property, out var element)) return null;
        if (element.ValueKind == JsonValueKind.String) return element.GetString();
        issues.Add(new("event_field_invalid", source, $"{property} is invalid.", "partial event retained"));
        return null;
    }

    private static IReadOnlyList<int> OptionalParticipantArray(JsonElement parent, string property, string source, List<SourceDataIssue> issues)
    {
        if (!parent.TryGetProperty(property, out var element)) return [];
        if (element.ValueKind != JsonValueKind.Array)
        {
            issues.Add(new("event_field_invalid", source, $"{property} is invalid.", "partial event retained"));
            return [];
        }
        var result = new List<int>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.TryGetInt32(out var value)) result.Add(value);
            else issues.Add(new("event_field_invalid", source, $"{property} contains an invalid participant.", "invalid participant omitted"));
        }
        return result;
    }

    private static string SourceText(SourceEventReference source) => $"frame[{source.FrameIndex}].events[{source.EventIndex}]";
    private static ReconstructionMappingException Failure(string code, string message) => new(code, message);
}

public sealed class ReconstructionMappingException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}
