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
export type MatchList = { matches: Match[]; incompleteImports: Match[] };
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
export const getMatches = () => request<MatchList>("/api/matches?limit=50&offset=0");
export const getMatch = (matchId: string) => request<Match>(`/api/matches/${encodeURIComponent(matchId)}`);
export const fetchLatestMatches = () => request<FetchResult>("/api/matches/fetch", { method: "POST" });
