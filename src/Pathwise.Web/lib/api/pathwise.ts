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
export type MatchFieldFact<T> = { value: T; source: { jsonPath: string } };
export type MatchParticipantResult = {
  participantId: number;
  teamId: number;
  won: MatchFieldFact<boolean>;
  gameEndedInSurrender: MatchFieldFact<boolean> | null;
  gameEndedInEarlySurrender: MatchFieldFact<boolean> | null;
  nexusKills: MatchFieldFact<number> | null;
  nexusTakedowns: MatchFieldFact<number> | null;
  nexusLost: MatchFieldFact<number> | null;
};
export type ProgressionEvent =
  | { kind: "buildingDestroyed"; source: SourceEvent; timestampMs: number; buildingType: string | null; towerType: string | null; laneType: string | null; structureOwnerTeam: TeamAttribution; killerParticipantId: number | null; assistingParticipantIds: number[]; position: Position | null }
  | { kind: "riftHeraldKilled"; source: SourceEvent; timestampMs: number; killerParticipantId: number | null; teamAttribution: TeamAttribution; assistingParticipantIds: number[]; position: Position | null }
  | { kind: "itemDestroyed"; source: SourceEvent; timestampMs: number; participantId: number | null; itemId: number }
  | { kind: "gameEnded"; source: SourceEvent; timestampMs: number; winningTeam: TeamAttribution };
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
  progression: { containsGameEnd: boolean; events: ProgressionEvent[] };
  positionSamples: ReviewPositionSample[];
  encounters: Encounter[];
};
export type MatchReview = {
  matchId: string;
  mapId: number;
  configuredParticipantId: number;
  participants: ReviewParticipant[];
  enemyResolution: { status: "resolved" | "missing" | "ambiguous"; participantId: number | null };
  versions: { reconstruction: number; detector: number; factualObservations: number; knowledgeAnnotations: number; encounters: number; progression: number };
  knowledge: { publicPatch: string | null; coverage: "available" | "unknownPatch" | "noPackForPatch" | "unsupportedMatch" };
  progression: {
    outcome: {
      configuredParticipantId: number;
      configuredTeamId: number;
      configuredPlayerWon: MatchFieldFact<boolean> | null;
      resolvedWinningTeamId: number | null;
      reportedDurationSeconds: MatchFieldFact<number>;
      matchEndTimestampMs: MatchFieldFact<number> | null;
      endOfGameResult: MatchFieldFact<string> | null;
      teamResults: { teamId: number; won: MatchFieldFact<boolean> }[];
      participantResults: MatchParticipantResult[];
      timelineGameEnd: Extract<ProgressionEvent, { kind: "gameEnded" }> | null;
    };
    events: ProgressionEvent[];
  };
  sourceDataIssues: SourceDataIssue[];
  windows: ReviewWindow[];
};

export type NarrativeClaim = { text: string; basis: "factSummary" | "crossEvidenceSynthesis"; evidenceIds: string[] };
export type NarrativeUncertainty = {
  statement: string;
  reason: "notCaptured" | "missingSource" | "ambiguousAttribution" | "samplingGap" | "conflictingSources" | "outsideNarrativeScope";
  relatedEvidenceIds: string[];
};
export type NarrativeThread = {
  title: string;
  startTimestampMs: number;
  endTimestampMs: number;
  summary: NarrativeClaim;
  significantDevelopments: NarrativeClaim[];
};
export type NarrativeInvestigationMoment = {
  title: string;
  startTimestampMs: number;
  endTimestampMs: number;
  whyItStandsOut: NarrativeClaim;
  question: string;
  uncertainties: NarrativeUncertainty[];
};
export type NarrativeInterpretation = {
  version: number;
  inputFingerprint: string;
  upstreamVersions: MatchReview["versions"];
  promptPolicyVersion: number;
  generation: { provider: string; model: string; generatedAtUtc: string };
  window: {
    requestedStartTimestampMs: number;
    requestedEndTimestampMs: number;
    startFrame: SourceFrame;
    endFrame: SourceFrame;
    primarySelectionReason: SelectionReason;
    signalKinds: SignalKind[];
    absorbedSignalKinds: SignalKind[];
  };
  overview: NarrativeClaim;
  threads: NarrativeThread[];
  momentsWorthInvestigating: NarrativeInvestigationMoment[];
  uncertainties: NarrativeUncertainty[];
};

export type NarrativeInterpretationRequest = {
  requestedStartTimestampMs: number;
  requestedEndTimestampMs: number;
  reconstructionVersion: number;
  detectorVersion: number;
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
export const interpretMatchReviewPeriod = (matchId: string, body: NarrativeInterpretationRequest, signal?: AbortSignal) => request<NarrativeInterpretation>(
  `/api/matches/${encodeURIComponent(matchId)}/review/interpretation`,
  { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body), signal },
);
export const fetchLatestMatches = () => request<FetchResult>("/api/matches/fetch", { method: "POST" });
