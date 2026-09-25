export type Player = {
  gameName: string;
  tagLine: string;
  platform: string;
  regional: string;
  fetchCount: number;
  fetchReady: boolean;
  configurationErrors: string[];
  puuid: string | null;
  resolvedAtUtc: string | null;
};

export type MatchFailure = { resource: string; code: string; message: string; httpStatus: number | null; retryAfterUtc: string | null };
export type Match = {
  matchId: string;
  queueId: number | null;
  playedAtUtc: string | null;
  durationSeconds: number | null;
  championName: string | null;
  won: boolean | null;
  teamPosition: string | null;
  supportStatus: "jungle" | "unsupported" | "unknown";
  ingestionStatus: "complete" | "incomplete";
  matchPayloadAvailable: boolean;
  timelinePayloadAvailable: boolean;
  failures: MatchFailure[];
};
export type MatchList = { totalStored: number; limit: number; offset: number; matches: Match[]; incompleteImports: Match[] };
export type FetchResult = {
  outcome: "succeeded" | "partial" | "failed";
  requestedCount: number;
  discoveredCount: number;
  newlyCompleted: string[];
  repaired: string[];
  alreadyComplete: string[];
  incomplete: string[];
  deferred: string[];
  errors: { matchId: string | null; resource: string; code: string; message: string; httpStatus: number | null }[];
  retryAfterUtc: string | null;
};

export type MetricKind = "relativeGoldMovement" | "relativeXpMovement" | "relativeJungleCsMovement";
export type SelectionReason = "goldAndXpChange" | "goldChange" | "xpChange" | "configuredPlayerDeath" | "concentratedPlayerCombat" | "eliteMonsterKill";
export type SignalKind = "goldDifferenceChange" | "xpDifferenceChange" | "configuredPlayerDeath" | "concentratedPlayerCombat" | "eliteMonsterKill";
export type SourceFrame = { frameIndex: number; timestampMs: number };
export type SourceEvent = { frameIndex: number; eventIndex: number };
export type Position = { x: number; y: number };
export type ReviewParticipant = { participantId: number; championName: string; teamId: number };
export type ReviewPositionSample = { frameIndex: number; timestampMs: number; configuredPlayerPosition: Position | null; enemyJunglerPosition: Position | null };
export type SourceDataIssue = { code: string; sourceReference: string; explanation: string; handlingOutcome: string };
export type TeamAttribution = { kind: "KnownTeam" | "Neutral" | "Unknown"; suppliedTeamId: number | null; resolvedTeamId: number | null; diagnosticReason: string | null };
export type MetricValues = {
  relativeStart: number;
  relativeEnd: number;
  signedChange: number;
  configuredPlayer: { startValue: number; endValue: number };
  enemyJungler: { startValue: number; endValue: number };
};
export type CombatEvent = { source: SourceEvent; timestampMs: number; killerParticipantId: number | null; victimParticipantId: number; assistingParticipantIds: number[]; position: Position | null };
export type ObjectiveEvent = { source: SourceEvent; timestampMs: number; monsterType: string | null; monsterSubType: string | null; killerParticipantId: number | null; assistingParticipantIds: number[]; teamAttribution: TeamAttribution; position: Position | null };
export type Encounter = {
  id: string; startTimestampMs: number; endTimestampMs: number; recordedEventSpanMs: number;
  combatEventCount: number; participantIds: number[]; distinctParticipantCount: number;
  configuredPlayerSummary: { involved: boolean; kills: number; deaths: number; assists: number; distinctEventCount: number };
  enemyJunglerInvolved: boolean | null; combatEvents: CombatEvent[]; associatedObjectiveEvents: ObjectiveEvent[];
};
export type Observation =
  | ({ kind: "relativeGoldMovement" | "relativeXpMovement" | "relativeJungleCsMovement" } & MetricValues)
  | { kind: "configuredPlayerCombat"; kills: number; deaths: number; assists: number; distinctEventCount: number; events: CombatEvent[] }
  | { kind: "eliteObjectiveContext"; events: ObjectiveEvent[] };
export type KnowledgeFact = { id: string; objective: "elementalDragon" | "baronNashor"; initialSpawnTimestampMs: number; sources: { title: string; url: string }[] };
export type KnowledgeAnnotation =
  | { kind: "nearInitialSpawn"; fact: KnowledgeFact; target: { observationKind: null; event: null }; recordedKillTimestampMs: null }
  | { kind: "recordedObjectiveContext"; fact: KnowledgeFact; target: { observationKind: "eliteObjectiveContext"; event: SourceEvent }; recordedKillTimestampMs: number };
export type ReviewWindow = {
  requestedStartTimestampMs: number;
  requestedEndTimestampMs: number;
  startFrame: SourceFrame;
  endFrame: SourceFrame;
  selectionRank: number;
  primarySelectionReason: SelectionReason;
  signalKinds: SignalKind[];
  absorbedSignalKinds: SignalKind[];
  observations: Observation[];
  metricOmissions: { kind: MetricKind; reason: "counterRegression" }[];
  knowledgeAnnotations: KnowledgeAnnotation[];
  positionSamples: ReviewPositionSample[];
  encounters: Encounter[];
};
export type MatchReview = {
  matchId: string;
  mapId: number;
  configuredParticipantId: number;
  participants: ReviewParticipant[];
  enemyResolution: { status: "resolved" | "missing" | "ambiguous"; participantId: number | null };
  versions: { reconstruction: number; detector: number; factualObservations: number; knowledgeAnnotations: number; encounters: number };
  knowledge: { publicPatch: string | null; coverage: "available" | "unknownPatch" | "noPackForPatch" | "unsupportedMatch" };
  sourceDataIssues: SourceDataIssue[];
  windows: ReviewWindow[];
};

const apiBaseUrl = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5100";

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${apiBaseUrl}${path}`, { cache: "no-store", ...init });
  if (!response.ok) {
    const problem = (await response.json().catch(() => null)) as { title?: string; detail?: string } | null;
    throw new Error(problem?.detail ?? problem?.title ?? `Pathwise API returned ${response.status}.`);
  }
  return response.json() as Promise<T>;
}

export const getPlayer = () => request<Player>("/api/player");
export const getMatches = (limit = 50, offset = 0) => request<MatchList>(`/api/matches?limit=${limit}&offset=${offset}`);
export const getMatch = (matchId: string) => request<Match>(`/api/matches/${encodeURIComponent(matchId)}`);
export const getMatchReview = (matchId: string, signal?: AbortSignal) => request<MatchReview>(`/api/matches/${encodeURIComponent(matchId)}/review`, { signal });
export const fetchLatestMatches = () => request<FetchResult>("/api/matches/fetch", { method: "POST" });
