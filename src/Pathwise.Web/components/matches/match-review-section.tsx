"use client";

import { useEffect, useState } from "react";
import { Button } from "@/components/ui/button";
import { ReviewWindowMap } from "./review-window-map";
import type { EvidenceLayer } from "./spatial-evidence";
import {
  getMatchReview,
  type KnowledgeAnnotation,
  type MatchReview,
  type MetricKind,
  type ObjectiveEvent,
  type Observation,
  type ReviewWindow,
  type SelectionReason,
  type SignalKind,
  type SourceEvent,
} from "@/lib/api/pathwise";

const selectionLabels: Record<SelectionReason, string> = {
  goldAndXpChange: "relative gold and XP change",
  goldChange: "relative gold change",
  xpChange: "relative XP change",
  configuredPlayerDeath: "recorded player death",
  concentratedPlayerCombat: "concentrated combat events",
  eliteMonsterKill: "recorded elite objective",
};

const metricLabels: Record<MetricKind, string> = {
  relativeGoldMovement: "Relative gold",
  relativeXpMovement: "Relative XP",
  relativeJungleCsMovement: "Relative Jungle CS",
};

const signalLabels: Record<SignalKind, string> = {
  goldDifferenceChange: "Gold difference change",
  xpDifferenceChange: "XP difference change",
  configuredPlayerDeath: "Configured player death",
  concentratedPlayerCombat: "Concentrated player combat",
  eliteMonsterKill: "Elite monster kill",
};

export function MatchReviewSection({ matchId }: { matchId: string }) {
  const [attempt, setAttempt] = useState(0);
  const [result, setResult] = useState<{ key: string; review: MatchReview | null; error: string | null } | null>(null);
  const requestKey = `${matchId}:${attempt}`;
  const current = result?.key === requestKey ? result : null;

  useEffect(() => {
    const controller = new AbortController();
    void getMatchReview(matchId, controller.signal)
      .then((review) => setResult({ key: requestKey, review, error: null }))
      .catch((cause: unknown) => {
        if (!controller.signal.aborted) setResult({ key: requestKey, review: null, error: cause instanceof Error ? cause.message : "Could not load this match review." });
      });
    return () => controller.abort();
  }, [matchId, requestKey]);

  return (
    <section aria-labelledby="match-review-heading" className="mt-8">
      <div className="mb-5">
        <h2 id="match-review-heading" className="text-xl font-semibold tracking-tight">Match review</h2>
        <p className="mt-2 text-sm leading-6 text-muted-foreground">Selected periods with factual observations and game context.</p>
      </div>
      {current?.error ? (
        <div className="rounded-xl border border-destructive/40 bg-destructive/5 p-5" role="alert">
          <p className="font-medium">Match review unavailable</p>
          <p className="mt-2 text-sm text-muted-foreground">{current.error}</p>
          <Button className="mt-4" variant="outline" onClick={() => setAttempt((value) => value + 1)}>Retry</Button>
        </div>
      ) : !current?.review ? (
        <div className="rounded-xl border bg-card p-5 text-sm text-muted-foreground">Loading match review…</div>
      ) : (
        <MatchReviewContent key={current.review.matchId} review={current.review} />
      )}
    </section>
  );
}

export function MatchReviewContent({ review }: { review: MatchReview }) {
  const [selectedIndex, setSelectedIndex] = useState(0);
  const [enabledLayers, setEnabledLayers] = useState<Record<EvidenceLayer, boolean>>({ you: true, enemy: true, combat: true, objectives: true });
  const windows = [...review.windows].sort((left, right) =>
    left.requestedStartTimestampMs - right.requestedStartTimestampMs || left.requestedEndTimestampMs - right.requestedEndTimestampMs);
  const selectedWindow = windows[selectedIndex] ?? windows[0];
  return (
    <div className="space-y-5">
      {review.enemyResolution.status !== "resolved" ? (
        <p className="rounded-lg border border-warning/30 bg-warning/5 px-4 py-3 text-sm text-warning">Relative metrics are unavailable because the opposing jungler could not be resolved.</p>
      ) : (
        <p className="text-sm text-muted-foreground">Relative values compare your champion with the opposing jungler.</p>
      )}
      <KnowledgeCoverage review={review} />
      {windows.length === 0 ? <p className="rounded-xl border bg-card p-5 text-sm text-muted-foreground">No review periods were selected for this match.</p> : null}
      {selectedWindow ? <>
        <div className="max-w-xl">
          <label htmlFor="review-period" className="mb-2 block text-sm font-medium">Review period</label>
          <select id="review-period" value={selectedIndex} onChange={(event) => setSelectedIndex(Number(event.target.value))} className="min-h-11 w-full rounded-md border bg-card px-3 text-sm focus-visible:outline-2 focus-visible:outline-ring">
            {windows.map((window, index) => <option key={`${window.requestedStartTimestampMs}-${window.requestedEndTimestampMs}-${window.selectionRank}`} value={index}>{formatMatchTime(window.requestedStartTimestampMs)}–{formatMatchTime(window.requestedEndTimestampMs)} · {selectionLabels[window.primarySelectionReason]}</option>)}
          </select>
        </div>
        <ReviewWindowCard key={`${selectedWindow.requestedStartTimestampMs}-${selectedWindow.requestedEndTimestampMs}-${selectedWindow.selectionRank}`} window={selectedWindow} review={review} patch={review.knowledge.publicPatch} knowledgeAvailable={review.knowledge.coverage === "available"} enabledLayers={enabledLayers} onToggleLayer={(layer) => setEnabledLayers((current) => ({ ...current, [layer]: !current[layer] }))} />
      </> : null}
      {review.sourceDataIssues.length > 0 ? (
        <details className="rounded-xl border border-warning/30 bg-warning/5 p-4 text-sm">
          <summary className="cursor-pointer font-medium text-warning">Source-data notices ({review.sourceDataIssues.length})</summary>
          <div className="mt-4 space-y-3 text-muted-foreground">{review.sourceDataIssues.map((issue, index) => <div key={`${issue.code}-${issue.sourceReference}-${index}`}><p className="font-medium text-foreground">{issue.code} · {issue.sourceReference}</p><p>{issue.explanation}</p><p>{issue.handlingOutcome}</p></div>)}</div>
        </details>
      ) : null}
    </div>
  );
}

function KnowledgeCoverage({ review }: { review: MatchReview }) {
  const { coverage, publicPatch } = review.knowledge;
  if (coverage === "available") return null;
  const message = coverage === "unknownPatch"
    ? "Game context unavailable: the public patch could not be resolved."
    : coverage === "noPackForPatch"
      ? `Game context unavailable: no knowledge pack exists for patch ${publicPatch}.`
      : "Game context unavailable: the knowledge pack does not cover this match’s map or queue.";
  return <p className="rounded-lg border bg-muted/30 px-4 py-3 text-sm text-muted-foreground">{message}</p>;
}

function ReviewWindowCard({ window, review, patch, knowledgeAvailable, enabledLayers, onToggleLayer }: { window: ReviewWindow; review: MatchReview; patch: string | null; knowledgeAvailable: boolean; enabledLayers: Record<EvidenceLayer, boolean>; onToggleLayer: (layer: EvidenceLayer) => void }) {
  const metrics = window.observations.filter(isMetricObservation);
  const combat = window.observations.find((observation) => observation.kind === "configuredPlayerCombat");
  const objectives = window.observations.find((observation) => observation.kind === "eliteObjectiveContext");
  return (
    <article className="rounded-xl border bg-card p-5 sm:p-6">
      <header>
        <h3 className="text-lg font-semibold tabular-nums">{formatMatchTime(window.requestedStartTimestampMs)}–{formatMatchTime(window.requestedEndTimestampMs)}</h3>
        <p className="mt-1 text-sm text-muted-foreground">Selected for {selectionLabels[window.primarySelectionReason]}</p>
      </header>

      <div className="mt-6 space-y-3">
        <h4 className="text-xs font-medium uppercase tracking-wider text-muted-foreground">Observed changes</h4>
        {window.observations.length === 0 ? <p className="text-sm text-muted-foreground">No factual observations were emitted for this period.</p> : null}
        {metrics.map((observation) => <MetricLine key={observation.kind} observation={observation} />)}
        {combat?.kind === "configuredPlayerCombat" ? <p className="text-sm"><span className="font-medium">Combat:</span> {formatCount(combat.kills, "kill")} / {formatCount(combat.deaths, "death")} / {formatCount(combat.assists, "assist")}</p> : null}
        {objectives?.kind === "eliteObjectiveContext" ? <p className="text-sm"><span className="font-medium">Objectives:</span> {objectives.events.map(objectiveName).join(", ")}</p> : null}
        {window.metricOmissions.map((omission) => <p key={omission.kind} className="rounded-md bg-warning/10 px-3 py-2 text-xs text-warning">{metricLabels[omission.kind]} was omitted because the source counters were inconsistent.</p>)}
      </div>

      {knowledgeAvailable ? <KnowledgeContext annotations={window.knowledgeAnnotations} objectives={objectives?.kind === "eliteObjectiveContext" ? objectives.events : []} patch={patch} /> : null}
      <ReviewWindowMap review={review} window={window} enabled={enabledLayers} onToggle={onToggleLayer} />
      <EvidenceDisclosure window={window} metrics={metrics} />
    </article>
  );
}

function MetricLine({ observation }: { observation: Extract<Observation, { kind: MetricKind }> }) {
  return <p className="flex flex-wrap justify-between gap-x-4 gap-y-1 text-sm"><span className="font-medium">{metricLabels[observation.kind]}:</span><span className="tabular-nums">{formatSigned(observation.relativeStart)} → {formatSigned(observation.relativeEnd)} ({formatSigned(observation.signedChange)})</span></p>;
}

function KnowledgeContext({ annotations, objectives, patch }: { annotations: KnowledgeAnnotation[]; objectives: ObjectiveEvent[]; patch: string | null }) {
  return (
    <div className="mt-5 border-t border-border/60 pt-4 text-muted-foreground">
      <h4 className="text-[11px] font-medium uppercase tracking-wider">Game context{patch ? ` · Patch ${patch}` : ""}</h4>
      {annotations.length === 0 ? <p className="mt-2 text-xs">No game-context annotations apply to this period.</p> : (
        <div className="mt-2 space-y-3">{annotations.map((annotation, index) => {
          const recordedEvent = annotation.kind === "recordedObjectiveContext" ? objectives.find((event) => sameSource(event.source, annotation.target.event)) : undefined;
          if (annotation.kind === "nearInitialSpawn") {
            return <p key={`${annotation.kind}-${annotation.fact.id}-${index}`} className="text-xs leading-5">Initial-spawn context · {knowledgeObjectiveName(annotation.fact.objective)} initial spawn: <span className="tabular-nums">{formatMatchTime(annotation.fact.initialSpawnTimestampMs)}</span></p>;
          }
          return <div key={`${annotation.kind}-${annotation.fact.id}-${index}`} className="text-xs">
            <p className="font-medium text-foreground/80">Associated recorded kill: {recordedEvent ? objectiveName(recordedEvent) : knowledgeObjectiveName(annotation.fact.objective)} at {formatMatchTime(annotation.recordedKillTimestampMs)}</p>
            <p className="mt-1">{knowledgeObjectiveName(annotation.fact.objective)} initial spawn: <span className="tabular-nums">{formatMatchTime(annotation.fact.initialSpawnTimestampMs)}</span></p>
          </div>;
        })}</div>
      )}
    </div>
  );
}

function EvidenceDisclosure({ window, metrics }: { window: ReviewWindow; metrics: Extract<Observation, { kind: MetricKind }>[] }) {
  const combat = window.observations.find((observation) => observation.kind === "configuredPlayerCombat");
  const objectives = window.observations.find((observation) => observation.kind === "eliteObjectiveContext");
  const sources = window.knowledgeAnnotations.flatMap((annotation) => annotation.fact.sources.map((source) => ({ ...source, factId: annotation.fact.id })));
  return (
    <details className="mt-6 border-t pt-4 text-sm">
      <summary className="cursor-pointer font-medium">Evidence and sources</summary>
      <div className="mt-4 space-y-4 text-muted-foreground">
        <div className="space-y-1 tabular-nums"><p>Review bounds: {formatMatchTime(window.requestedStartTimestampMs, true)}–{formatMatchTime(window.requestedEndTimestampMs, true)}</p>{metrics.length > 0 || window.metricOmissions.length > 0 ? <p>Metric frames: {formatMatchTime(window.startFrame.timestampMs, true)} (frame {window.startFrame.frameIndex})–{formatMatchTime(window.endFrame.timestampMs, true)} (frame {window.endFrame.frameIndex})</p> : null}<p>Events after the start, through the end.</p></div>
        <div><p className="font-medium text-foreground">Detector selection information</p><p>Selection rank: {window.selectionRank}</p><p>Signal types: {window.signalKinds.length ? window.signalKinds.map((kind) => signalLabels[kind]).join(", ") : "None"}</p><p>Absorbed signal types: {window.absorbedSignalKinds.length ? window.absorbedSignalKinds.map((kind) => signalLabels[kind]).join(", ") : "None"}</p></div>
        {metrics.map((metric) => <div key={metric.kind}><p className="font-medium text-foreground">{metricLabels[metric.kind]} absolute endpoints</p><p>Configured player: {formatNumber(metric.configuredPlayer.startValue)} → {formatNumber(metric.configuredPlayer.endValue)}</p><p>Enemy jungler: {formatNumber(metric.enemyJungler.startValue)} → {formatNumber(metric.enemyJungler.endValue)}</p></div>)}
        {combat?.kind === "configuredPlayerCombat" && combat.events.length ? <div><p className="font-medium text-foreground">Combat event references</p>{combat.events.map((event) => <p key={`${event.source.frameIndex}-${event.source.eventIndex}`}>{formatMatchTime(event.timestampMs, true)} · frame {event.source.frameIndex}, event {event.source.eventIndex}</p>)}</div> : null}
        {objectives?.kind === "eliteObjectiveContext" && objectives.events.length ? <div><p className="font-medium text-foreground">Objective event references</p>{objectives.events.map((event) => <p key={`${event.source.frameIndex}-${event.source.eventIndex}`}>{objectiveName(event)} · {formatMatchTime(event.timestampMs, true)} · frame {event.source.frameIndex}, event {event.source.eventIndex} · attribution {event.teamAttribution.kind}</p>)}</div> : null}
        {sources.length ? <div><p className="font-medium text-foreground">Mechanic sources</p><ul className="mt-1 list-disc space-y-1 pl-5">{sources.map((source, index) => <li key={`${source.factId}-${source.url}-${index}`}><a className="text-primary underline underline-offset-4" href={source.url} target="_blank" rel="noreferrer">{source.title}</a></li>)}</ul></div> : null}
      </div>
    </details>
  );
}

function isMetricObservation(observation: Observation): observation is Extract<Observation, { kind: MetricKind }> {
  return observation.kind === "relativeGoldMovement" || observation.kind === "relativeXpMovement" || observation.kind === "relativeJungleCsMovement";
}

function sameSource(left: SourceEvent, right: SourceEvent) { return left.frameIndex === right.frameIndex && left.eventIndex === right.eventIndex; }
export function formatMatchTime(timestampMs: number, exact = false) {
  const floored = Math.floor(timestampMs);
  const totalSeconds = Math.floor(floored / 1000);
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = String(totalSeconds % 60).padStart(2, "0");
  return exact ? `${minutes}:${seconds}.${String(floored % 1000).padStart(3, "0")}` : `${minutes}:${seconds}`;
}
export function formatSigned(value: number) { return value > 0 ? `+${formatNumber(value)}` : value < 0 ? `−${formatNumber(Math.abs(value))}` : "0"; }
function formatNumber(value: number) { return new Intl.NumberFormat("en-US").format(value); }
function formatCount(value: number, singular: string) { return `${value} ${singular}${value === 1 ? "" : "s"}`; }
function knowledgeObjectiveName(objective: "elementalDragon" | "baronNashor") { return objective === "elementalDragon" ? "Elemental Dragon" : "Baron Nashor"; }
export function objectiveName(event: ObjectiveEvent) {
  if (event.monsterType === "BARON_NASHOR") return "Baron Nashor";
  const subtype = event.monsterSubType ?? event.monsterType ?? "Unknown objective";
  const names: Record<string, string> = { AIR_DRAGON: "Cloud Dragon", EARTH_DRAGON: "Mountain Dragon", FIRE_DRAGON: "Infernal Dragon", WATER_DRAGON: "Ocean Dragon", HEXTECH_DRAGON: "Hextech Dragon", CHEMTECH_DRAGON: "Chemtech Dragon", ELDER_DRAGON: "Elder Dragon" };
  return names[subtype] ?? subtype;
}
