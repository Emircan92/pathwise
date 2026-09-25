import type { CombatEvent, Encounter, MatchReview, ObjectiveEvent, Position, ReviewWindow } from "@/lib/api/pathwise";

export type EvidenceLayer = "you" | "enemy" | "combat" | "objectives";
export type SpatialEvidence = {
  id: string;
  layer: EvidenceLayer;
  timestampMs: number;
  label: string;
  position: Position | null;
  kind: "sample" | "event";
  boundary: boolean;
  description: string;
  frameIndex: number;
  eventIndex: number;
};

function champion(review: MatchReview, id: number | null): string {
  if (id === null || id === 0) return "Unknown participant";
  return review.participants.find((participant) => participant.participantId === id)?.championName ?? `Participant ${id}`;
}

function objectiveName(event: ObjectiveEvent): string {
  const names: Record<string, string> = {
    AIR_DRAGON: "Cloud Dragon", EARTH_DRAGON: "Mountain Dragon", FIRE_DRAGON: "Infernal Dragon",
    WATER_DRAGON: "Ocean Dragon", HEXTECH_DRAGON: "Hextech Dragon", CHEMTECH_DRAGON: "Chemtech Dragon",
    ELDER_DRAGON: "Elder Dragon", BARON_NASHOR: "Baron Nashor", RIFTHERALD: "Rift Herald", HORDE: "Void Grub",
  };
  return names[event.monsterSubType ?? event.monsterType ?? ""] ?? event.monsterSubType ?? event.monsterType ?? "Unknown objective";
}

function combatLabel(event: CombatEvent, playerId: number): string {
  if (event.victimParticipantId === playerId) return "You died";
  if (event.killerParticipantId === playerId) return "You killed a champion";
  if (event.assistingParticipantIds.includes(playerId)) return "You assisted a kill";
  return "Recorded champion kill";
}

export function buildSpatialEvidence(review: MatchReview, window: ReviewWindow, encounter: Encounter | null = null): SpatialEvidence[] {
  const windowKey = `${window.requestedStartTimestampMs}-${window.requestedEndTimestampMs}`;
  const entries: SpatialEvidence[] = [];
  const nearbyStart = encounter === null ? Number.NEGATIVE_INFINITY : Math.max(window.requestedStartTimestampMs, encounter.startTimestampMs - 30_000);
  const nearbyEnd = encounter === null ? Number.POSITIVE_INFINITY : Math.min(window.requestedEndTimestampMs, encounter.endTimestampMs + 30_000);
  // A later frame can be after respawn; the timeline has no respawn event to tie it to this encounter.
  const firstDeath = (participantId: number) => encounter?.combatEvents
    .filter((event) => event.victimParticipantId === participantId)
    .reduce<number | null>((earliest, event) => earliest === null ? event.timestampMs : Math.min(earliest, event.timestampMs), null) ?? null;
  const playerDeath = firstDeath(review.configuredParticipantId);
  const enemyId = review.enemyResolution.status === "resolved" ? review.enemyResolution.participantId : null;
  const enemyDeath = enemyId === null ? null : firstDeath(enemyId);
  for (const sample of window.positionSamples.filter((sample) => sample.timestampMs >= nearbyStart && sample.timestampMs <= nearbyEnd)) {
    const boundary = sample.timestampMs <= window.requestedStartTimestampMs;
    if (playerDeath === null || sample.timestampMs < playerDeath) {
      entries.push({ id: `${windowKey}:sample:${sample.frameIndex}:${review.configuredParticipantId}`, layer: "you", timestampMs: sample.timestampMs,
        label: encounter ? "Nearby frame sample · You" : "You", position: sample.configuredPlayerPosition, kind: "sample", boundary, frameIndex: sample.frameIndex, eventIndex: -1,
        description: encounter ? `${champion(review, review.configuredParticipantId)} · Nearby frame sample; this does not establish encounter participation or exact event presence` : `${champion(review, review.configuredParticipantId)} · Frame sample` });
    }
    if (enemyId !== null && (enemyDeath === null || sample.timestampMs < enemyDeath)) {
      entries.push({ id: `${windowKey}:sample:${sample.frameIndex}:${enemyId}`, layer: "enemy", timestampMs: sample.timestampMs,
        label: encounter ? "Nearby frame sample · Enemy jungler" : "Enemy jungler", position: sample.enemyJunglerPosition, kind: "sample", boundary, frameIndex: sample.frameIndex, eventIndex: -1,
        description: encounter ? `${champion(review, enemyId)} · Nearby frame sample; this does not establish encounter participation or exact event presence` : `${champion(review, enemyId)} · Frame sample` });
    }
  }
  const combatEvents = encounter ? encounter.combatEvents : [...new Map(window.encounters.flatMap((item) => item.combatEvents).map((event) => [`${event.source.frameIndex}:${event.source.eventIndex}`, event])).values()];
  const objectiveEvents = encounter ? encounter.associatedObjectiveEvents : window.observations.flatMap((observation) => observation.kind === "eliteObjectiveContext" ? observation.events : []);
  for (const event of combatEvents) entries.push({ id: `${windowKey}:combat:${event.source.frameIndex}:${event.source.eventIndex}`,
        layer: "combat", timestampMs: event.timestampMs, label: combatLabel(event, review.configuredParticipantId), position: event.position,
        kind: "event", boundary: false, frameIndex: event.source.frameIndex, eventIndex: event.source.eventIndex,
        description: `Killer: ${champion(review, event.killerParticipantId)} · Victim: ${champion(review, event.victimParticipantId)} · Assists: ${event.assistingParticipantIds.length ? event.assistingParticipantIds.map((id) => champion(review, id)).join(", ") : "none"} · Event-reported position` });
  for (const event of objectiveEvents) entries.push({ id: `${windowKey}:objective:${event.source.frameIndex}:${event.source.eventIndex}`,
        layer: "objectives", timestampMs: event.timestampMs, label: `${objectiveName(event)} killed`, position: event.position,
        kind: "event", boundary: false, frameIndex: event.source.frameIndex, eventIndex: event.source.eventIndex,
        description: `${objectiveName(event)} · Killer: ${champion(review, event.killerParticipantId)} · Attribution: ${event.teamAttribution.kind === "KnownTeam" ? `team ${event.teamAttribution.resolvedTeamId}` : event.teamAttribution.kind.toLowerCase()} · Event-reported position` });
  const order: Record<EvidenceLayer, number> = { you: 0, enemy: 1, combat: 2, objectives: 3 };
  return entries.sort((left, right) => left.timestampMs - right.timestampMs || order[left.layer] - order[right.layer] || left.frameIndex - right.frameIndex || left.eventIndex - right.eventIndex);
}

export function initialSpatialEvidence(entries: SpatialEvidence[], encounterSelected = false): string | null {
  return entries.find((entry) => entry.layer === "combat")?.id
    ?? (encounterSelected ? entries[0]?.id : null)
    ?? entries.find((entry) => entry.kind === "event" && !entry.boundary)?.id
    ?? entries.find((entry) => entry.kind === "sample" && !entry.boundary)?.id
    ?? entries[0]?.id ?? null;
}
