"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import { RefreshCw } from "lucide-react";
import { Button } from "@/components/ui/button";
import { fetchLatestMatches, getMatches, getPlayer, type FetchResult, type Match, type MatchList, type Player } from "@/lib/api/pathwise";

function formatDuration(seconds: number | null) {
  if (seconds === null) return "—";
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, "0")}`;
}

function MatchRow({ match }: { match: Match }) {
  const date = match.playedAtUtc ? new Intl.DateTimeFormat(undefined, {
    year: "numeric",
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
    timeZoneName: "short",
  }).format(new Date(match.playedAtUtc)) : "Date unavailable";
  return (
    <Link href={`/matches/${encodeURIComponent(match.matchId)}`} className="grid gap-4 border-t px-5 py-4 transition-colors hover:bg-accent/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring sm:grid-cols-[1.3fr_.65fr_.65fr_1fr_auto] sm:items-center">
      <div><p className="font-medium">{match.championName ?? match.matchId}</p><p className="mt-1 text-xs text-muted-foreground">{date}</p></div>
      <p className={match.won === true ? "font-medium text-success" : match.won === false ? "font-medium text-destructive" : "text-muted-foreground"}>{match.won === true ? "Win" : match.won === false ? "Loss" : "Pending"}</p>
      <p className="text-sm tabular-nums">{formatDuration(match.durationSeconds)}</p>
      <p className="text-sm text-muted-foreground">{match.teamPosition || "Unknown role"}</p>
      <div className="flex flex-wrap gap-2 sm:justify-end">
        <span className={match.supportStatus === "jungle" ? "rounded-full bg-primary/15 px-2.5 py-1 text-xs font-medium text-primary" : "rounded-full bg-muted px-2.5 py-1 text-xs text-muted-foreground"}>{match.supportStatus === "jungle" ? "Jungle · Supported" : "Unsupported"}</span>
        {match.ingestionStatus === "incomplete" ? <span className="rounded-full bg-warning/15 px-2.5 py-1 text-xs text-warning">Incomplete</span> : null}
      </div>
    </Link>
  );
}

export function RecentMatches() {
  const [player, setPlayer] = useState<Player | null>(null);
  const [data, setData] = useState<MatchList>({ totalStored: 0, limit: 50, offset: 0, matches: [], incompleteImports: [] });
  const [loadedRows, setLoadedRows] = useState(0);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [fetching, setFetching] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<FetchResult | null>(null);

  const load = useCallback(async () => {
    try {
      const [nextPlayer, nextMatches] = await Promise.all([getPlayer(), getMatches()]);
      setPlayer(nextPlayer); setData(nextMatches); setLoadedRows(nextMatches.matches.length + nextMatches.incompleteImports.length); setError(null);
    } catch (cause) { setError(cause instanceof Error ? cause.message : "Could not reach the Pathwise API."); }
    finally { setLoading(false); }
  }, []);

  useEffect(() => {
    let current = true;
    void Promise.all([getPlayer(), getMatches()])
      .then(([nextPlayer, nextMatches]) => {
        if (!current) return;
        setPlayer(nextPlayer); setData(nextMatches); setLoadedRows(nextMatches.matches.length + nextMatches.incompleteImports.length); setError(null);
      })
      .catch((cause: unknown) => { if (current) setError(cause instanceof Error ? cause.message : "Could not reach the Pathwise API."); })
      .finally(() => { if (current) setLoading(false); });
    return () => { current = false; };
  }, []);

  async function loadMore() {
    setLoadingMore(true); setError(null);
    try {
      const page = await getMatches(50, loadedRows);
      setData((current) => {
        const seen = new Set([...current.matches, ...current.incompleteImports].map((match) => match.matchId));
        return {
          ...page,
          matches: [...current.matches, ...page.matches.filter((match) => !seen.has(match.matchId))],
          incompleteImports: [...current.incompleteImports, ...page.incompleteImports.filter((match) => !seen.has(match.matchId))],
        };
      });
      setLoadedRows(page.offset + page.matches.length + page.incompleteImports.length);
    } catch (cause) { setError(cause instanceof Error ? cause.message : "Could not load more matches."); }
    finally { setLoadingMore(false); }
  }

  async function fetchMatches() {
    setFetching(true); setError(null); setResult(null);
    try { setResult(await fetchLatestMatches()); }
    catch (cause) { setError(cause instanceof Error ? cause.message : "Match fetching failed."); }
    finally { await load(); setFetching(false); }
  }

  return (
    <main className="min-h-screen px-5 py-8 sm:px-10 sm:py-12">
      <div className="mx-auto max-w-6xl space-y-8">
        <header className="flex flex-col gap-6 border-b pb-8 sm:flex-row sm:items-end sm:justify-between">
          <div><p className="text-sm font-semibold uppercase tracking-[0.22em] text-primary">Pathwise</p><h1 className="mt-3 text-3xl font-semibold tracking-tight sm:text-4xl">Recent ranked matches</h1><p className="mt-3 text-muted-foreground">{player ? `${player.gameName || "Unconfigured"}#${player.tagLine || "—"} · ${player.platform.toUpperCase()}` : loading ? "Loading configured Riot identity…" : "Riot identity unavailable"}</p></div>
          <Button onClick={fetchMatches} disabled={fetching || !player?.fetchReady} className="min-w-48"><RefreshCw className={fetching ? "animate-spin" : ""} />{fetching ? "Fetching matches…" : "Fetch Latest Matches"}</Button>
        </header>

        {fetching ? <div className="rounded-xl border bg-card p-4 text-sm text-muted-foreground">A first import can take several minutes. Stored matches remain available while Pathwise retrieves missing data.</div> : null}
        {player && !player.fetchReady ? <div className="rounded-xl border border-warning/40 bg-warning/10 p-4"><p className="font-medium text-warning">Riot configuration needed</p><ul className="mt-2 list-disc space-y-1 pl-5 text-sm text-muted-foreground">{player.configurationErrors.map((item) => <li key={item}>{item}</li>)}</ul></div> : null}
        {error ? <div role="alert" className="rounded-xl border border-destructive/40 bg-destructive/10 p-4 text-sm text-destructive">{error}</div> : null}
        {result ? <div className="rounded-xl border bg-card p-4"><p className="font-medium capitalize">Fetch {result.outcome}</p><p className="mt-1 text-sm text-muted-foreground">Discovered {result.discoveredCount}; added {result.newlyCompleted.length}; repaired {result.repaired.length}; already complete {result.alreadyComplete.length}.</p>{result.errors.length ? <p className="mt-2 text-sm text-warning">{result.errors.length} resource failure(s). Fetch Latest Matches will retry incomplete resources that remain in the fetched set.</p> : null}</div> : null}

        <section className="overflow-hidden rounded-xl border bg-card"><div className="flex items-center justify-between px-5 py-4"><h2 className="font-semibold">Ranked Solo/Duo</h2><span className="text-sm text-muted-foreground">{data.totalStored} stored</span></div>{loading ? <p className="border-t p-8 text-center text-muted-foreground">Loading matches…</p> : data.matches.length ? data.matches.map((match) => <MatchRow key={match.matchId} match={match} />) : data.totalStored === 0 ? <p className="border-t p-10 text-center text-muted-foreground">No stored matches yet. Configure Riot and fetch your latest matches.</p> : null}</section>

        {data.incompleteImports.length ? <section className="rounded-xl border border-warning/30 bg-card p-5"><h2 className="font-semibold">Incomplete imports</h2><p className="mt-1 text-sm text-muted-foreground">These records lack enough metadata for the main list. A future Fetch Latest Matches run retries them if Riot returns them in the fetched set.</p><div className="mt-4 space-y-3">{data.incompleteImports.map((match) => <MatchRow key={match.matchId} match={match} />)}</div></section> : null}
        {loadedRows < data.totalStored ? <Button onClick={loadMore} disabled={loadingMore || fetching}>{loadingMore ? "Loading more…" : "Load more"}</Button> : null}
      </div>
    </main>
  );
}
