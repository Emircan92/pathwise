"use client";

import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { useEffect, useState } from "react";
import { getMatch, type Match } from "@/lib/api/pathwise";
import { MatchReviewSection } from "@/components/matches/match-review-section";

export function MatchDetail({ matchId }: { matchId: string }) {
  const [match, setMatch] = useState<Match | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => { void getMatch(matchId).then(setMatch).catch((cause: unknown) => setError(cause instanceof Error ? cause.message : "Could not load this match.")); }, [matchId]);

  if (error) return <DetailShell><p role="alert" className="text-destructive">{error}</p></DetailShell>;
  if (!match) return <DetailShell><p className="text-muted-foreground">Loading match…</p></DetailShell>;

  const duration = match.durationSeconds === null ? "—" : `${Math.floor(match.durationSeconds / 60)}:${String(match.durationSeconds % 60).padStart(2, "0")}`;
  const played = match.playedAtUtc ? new Intl.DateTimeFormat(undefined, {
    weekday: "long",
    year: "numeric",
    month: "long",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
    timeZoneName: "short",
  }).format(new Date(match.playedAtUtc)) : "Unavailable";
  return (
    <DetailShell>
      <div className="mx-auto max-w-4xl">
        <div className="flex flex-wrap items-center gap-3"><h1 className="text-3xl font-semibold tracking-tight">{match.championName ?? "Incomplete match"}</h1><span className={match.won ? "rounded-full bg-success/15 px-3 py-1 text-sm text-success" : "rounded-full bg-destructive/15 px-3 py-1 text-sm text-destructive"}>{match.won === null ? "Pending" : match.won ? "Win" : "Loss"}</span></div>
        <p className="mt-3 text-muted-foreground">{played}</p>
        <dl className="mt-8 grid gap-px overflow-hidden rounded-xl border bg-border sm:grid-cols-2 lg:grid-cols-4">
          {[ ["Duration", duration], ["Role", match.teamPosition || "Unknown"], ["Queue", match.queueId === 420 ? "Ranked Solo/Duo" : match.queueId ?? "Unknown"], ["Support", match.supportStatus === "jungle" ? "Jungle · Supported" : "Unsupported"] ].map(([label, value]) => <div key={label} className="bg-card p-5"><dt className="text-xs uppercase tracking-wider text-muted-foreground">{label}</dt><dd className="mt-2 font-medium">{value}</dd></div>)}
        </dl>
      </div>
      <MatchReviewSection key={matchId} matchId={matchId} matchDurationMs={match.durationSeconds === null ? null : match.durationSeconds * 1000} />
      <div className="mx-auto max-w-4xl">
        <section className="mt-8 rounded-xl border bg-card p-6"><h2 className="font-semibold">Source data</h2><div className="mt-4 grid gap-3 sm:grid-cols-2"><SourceStatus label="Match payload" available={match.matchPayloadAvailable} /><SourceStatus label="Timeline payload" available={match.timelinePayloadAvailable} /></div>{match.failures.length ? <div className="mt-5 space-y-2">{match.failures.map((failure) => <p key={`${failure.resource}-${failure.code}`} className="rounded-lg bg-warning/10 p-3 text-sm text-warning">{failure.resource}: {failure.message}</p>)}</div> : null}</section>
        <p className="mt-6 font-mono text-xs text-muted-foreground">{match.matchId}</p>
      </div>
    </DetailShell>
  );
}

function SourceStatus({ label, available }: { label: string; available: boolean }) { return <div className="flex items-center justify-between rounded-lg bg-muted/50 px-4 py-3 text-sm"><span>{label}</span><span className={available ? "text-success" : "text-warning"}>{available ? "Stored" : "Missing"}</span></div>; }
function DetailShell({ children }: { children: React.ReactNode }) { return <main className="min-h-screen px-5 py-8 sm:px-10 sm:py-12"><div className="mx-auto max-w-7xl"><div className="mx-auto max-w-4xl"><Link href="/" className="mb-8 inline-flex items-center gap-2 text-sm text-muted-foreground transition-colors hover:text-foreground"><ArrowLeft className="size-4" />Recent matches</Link></div>{children}</div></main>; }
