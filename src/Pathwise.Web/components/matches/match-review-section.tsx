"use client";

import { useEffect, useMemo, useState } from "react";
import { ChevronLeft, ChevronRight } from "lucide-react";
import { Button } from "@/components/ui/button";
import { ReviewWindowMap } from "./review-window-map";
import {
  buildSpatialEvidence,
  initialSpatialEvidence,
  spatialEvidenceLocationStatus,
  type EvidenceLayer,
  type SpatialEvidence,
} from "./spatial-evidence";
import {
  getMatchReview,
  type KnowledgeAnnotation,
  type Encounter,
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

const defaultLayers: Record<EvidenceLayer, boolean> = { you: true, enemy: true, combat: true, objectives: true };

export function MatchReviewSection({ matchId, matchDurationMs }: { matchId: string; matchDurationMs?: number | null }) {
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
        <MatchReviewContent key={current.review.matchId} review={current.review} matchDurationMs={matchDurationMs} />
      )}
    </section>
  );
}

export function MatchReviewContent({ review, matchDurationMs }: { review: MatchReview; matchDurationMs?: number | null }) {
  const windows = useMemo(() => [...review.windows].sort((left, right) =>
    left.requestedStartTimestampMs - right.requestedStartTimestampMs || left.requestedEndTimestampMs - right.requestedEndTimestampMs), [review.windows]);
  const initialWindowIndex = Math.max(0, windows.findIndex((window) => window.selectionRank === 1));
  const initialWindow = windows[initialWindowIndex];
  const [selectedIndex, setSelectedIndex] = useState(initialWindowIndex);
  const [encounterId, setEncounterId] = useState<string | null>(orderedEncounters(initialWindow)[0]?.id ?? null);
  const [selectedEvidenceId, setSelectedEvidenceId] = useState<string | null>(null);
  const [enabledLayers, setEnabledLayers] = useState<Record<EvidenceLayer, boolean>>(defaultLayers);
  const selectedWindow = windows[selectedIndex] ?? windows[0];
  const encounters = orderedEncounters(selectedWindow);
  const selectedEncounter = encounters.find((encounter) => encounter.id === encounterId) ?? null;
  const scopeEvidence = selectedWindow ? buildSpatialEvidence(review, selectedWindow, selectedEncounter) : [];
  const visibleEvidence = scopeEvidence.filter((entry) => enabledLayers[entry.layer]);
  const activeEvidenceId = visibleEvidence.some((entry) => entry.id === selectedEvidenceId)
    ? selectedEvidenceId
    : initialSpatialEvidence(visibleEvidence, selectedEncounter !== null);
  const activeEvidence = visibleEvidence.find((entry) => entry.id === activeEvidenceId) ?? null;

  function selectWindow(index: number) {
    const nextWindow = windows[index];
    if (!nextWindow) return;
    setSelectedIndex(index);
    setEncounterId(orderedEncounters(nextWindow)[0]?.id ?? null);
    setSelectedEvidenceId(null);
  }

  function selectEncounter(nextEncounterId: string | null) {
    setEncounterId(nextEncounterId);
    setSelectedEvidenceId(null);
  }

  function selectEvidence(id: string) {
    const evidence = scopeEvidence.find((entry) => entry.id === id);
    if (evidence && !enabledLayers[evidence.layer]) {
      setEnabledLayers((current) => ({ ...current, [evidence.layer]: true }));
    }
    setSelectedEvidenceId(id);
  }

  function selectEvidenceFromInspector(id: string) {
    if (selectedWindow && selectedEncounter === null) {
      const owner = encounters.find((encounter) => buildSpatialEvidence(review, selectedWindow, encounter)
        .some((entry) => entry.kind === "event" && entry.id === id));
      if (owner) setEncounterId(owner.id);
    }
    selectEvidence(id);
  }

  function toggleLayer(layer: EvidenceLayer) {
    if (enabledLayers[layer] && activeEvidence?.layer === layer) setSelectedEvidenceId(null);
    setEnabledLayers((current) => ({ ...current, [layer]: !current[layer] }));
  }

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
        <PeriodNavigator windows={windows} selectedIndex={selectedIndex} matchDurationMs={matchDurationMs} onSelect={selectWindow} />
        <article className="rounded-xl border bg-card p-4 sm:p-6">
          <div className="grid items-start gap-6 lg:grid-cols-[minmax(0,1.12fr)_minmax(22rem,0.88fr)] xl:gap-8">
            <div className="min-w-0">
              <PeriodContext window={selectedWindow} review={review} />
              <EncounterChapters
                encounters={encounters}
                review={review}
                window={selectedWindow}
                selectedEncounter={selectedEncounter}
                activeEvidenceId={activeEvidenceId}
                onSelectEncounter={selectEncounter}
                onSelectEvidence={selectEvidence}
              />
              <EvidenceDisclosure window={selectedWindow} metrics={selectedWindow.observations.filter(isMetricObservation)} />
            </div>
            <aside aria-label="Synchronized evidence inspector" className="min-w-0 lg:sticky lg:top-4 lg:max-h-[calc(100vh-2rem)] lg:overflow-y-auto lg:overscroll-contain lg:pr-1">
              <ScopeIndicator window={selectedWindow} encounter={selectedEncounter} encounterIndex={selectedEncounter ? encounters.findIndex((item) => item.id === selectedEncounter.id) : -1} evidence={activeEvidence} />
              <ReviewWindowMap
                review={review}
                window={selectedWindow}
                encounter={selectedEncounter}
                enabled={enabledLayers}
                activeId={activeEvidenceId}
                onToggle={toggleLayer}
                onActiveChange={selectEvidenceFromInspector}
              />
            </aside>
          </div>
        </article>
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

function PeriodNavigator({ windows, selectedIndex, matchDurationMs, onSelect }: { windows: ReviewWindow[]; selectedIndex: number; matchDurationMs?: number | null; onSelect: (index: number) => void }) {
  const selected = windows[selectedIndex];
  const timelineEnd = Math.max(matchDurationMs ?? 0, ...windows.map((window) => window.requestedEndTimestampMs), 1);
  const lanes = timelineLanes(windows);
  const laneCount = Math.max(...lanes, 0) + 1;
  return <nav aria-label="Review period navigation" className="rounded-xl border bg-card p-4 sm:p-5">
    <div className="flex flex-wrap items-center justify-between gap-3">
      <div>
        <p className="text-xs font-medium uppercase tracking-wider text-muted-foreground">Period {selectedIndex + 1} of {windows.length}</p>
        <p className="mt-1 font-semibold tabular-nums">{formatMatchTime(selected.requestedStartTimestampMs)}–{formatMatchTime(selected.requestedEndTimestampMs)}</p>
        <p className="mt-1 text-xs text-muted-foreground">Selected for {selectionLabels[selected.primarySelectionReason]}{selected.selectionRank === 1 ? " · Most important" : ""}</p>
      </div>
      <div className="flex gap-2">
        <Button type="button" variant="outline" size="sm" disabled={selectedIndex === 0} onClick={() => onSelect(selectedIndex - 1)} aria-label="Previous review period"><ChevronLeft aria-hidden="true" />Previous</Button>
        <Button type="button" variant="outline" size="sm" disabled={selectedIndex === windows.length - 1} onClick={() => onSelect(selectedIndex + 1)} aria-label="Next review period">Next<ChevronRight aria-hidden="true" /></Button>
      </div>
    </div>
    <div className="mt-4">
      <div className="mb-2 flex justify-between text-[11px] tabular-nums text-muted-foreground"><span>0:00</span><span>{formatMatchTime(timelineEnd)}</span></div>
      <div aria-label="Review periods across match time" className="relative rounded-full bg-muted" style={{ height: `${Math.max(20, laneCount * 14 + 6)}px` }}>
        {windows.map((window, index) => {
          const left = Math.min(100, Math.max(0, window.requestedStartTimestampMs / timelineEnd * 100));
          const width = Math.max(1.5, Math.min(100 - left, (window.requestedEndTimestampMs - window.requestedStartTimestampMs) / timelineEnd * 100));
          const active = index === selectedIndex;
          return <button key={windowKey(window)} type="button" onClick={() => onSelect(index)} aria-current={active ? "step" : undefined}
            aria-label={`Review period ${index + 1} of ${windows.length}, ${formatMatchTime(window.requestedStartTimestampMs)}–${formatMatchTime(window.requestedEndTimestampMs)}${window.selectionRank === 1 ? ", most important" : ""}`}
            className={`absolute h-2.5 min-w-3 rounded-full transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring ${active ? "bg-primary ring-2 ring-primary/30" : "bg-muted-foreground/35 hover:bg-muted-foreground/60"}`}
            style={{ left: `${left}%`, width: `${width}%`, top: `${3 + lanes[index] * 14}px`, zIndex: active ? 2 : 1 }} />;
        })}
      </div>
    </div>
  </nav>;
}

function PeriodContext({ window, review }: { window: ReviewWindow; review: MatchReview }) {
  const metrics = window.observations.filter(isMetricObservation);
  const combat = window.observations.find((observation) => observation.kind === "configuredPlayerCombat");
  const objectives = window.observations.find((observation) => observation.kind === "eliteObjectiveContext");
  return <section aria-labelledby="period-context-heading">
    <header>
      <p className="text-xs font-medium uppercase tracking-wider text-muted-foreground">Review window</p>
      <h3 id="period-context-heading" className="mt-1 text-xl font-semibold tabular-nums">{formatMatchTime(window.requestedStartTimestampMs)}–{formatMatchTime(window.requestedEndTimestampMs)}</h3>
      <p className="mt-1 text-sm text-muted-foreground">Selected for {selectionLabels[window.primarySelectionReason]}</p>
    </header>
    <div className="mt-5 rounded-lg border bg-muted/15 p-4">
      <h4 className="text-xs font-medium uppercase tracking-wider text-muted-foreground">Observed changes</h4>
      <div className="mt-3 space-y-3">
        {window.observations.length === 0 ? <p className="text-sm text-muted-foreground">No factual observations were emitted for this period.</p> : null}
        {metrics.map((observation) => <MetricLine key={observation.kind} observation={observation} />)}
        {combat?.kind === "configuredPlayerCombat" ? <p className="text-sm"><span className="font-medium">Combat:</span> {formatCount(combat.kills, "kill")} / {formatCount(combat.deaths, "death")} / {formatCount(combat.assists, "assist")}</p> : null}
        {objectives?.kind === "eliteObjectiveContext" ? <p className="text-sm"><span className="font-medium">Objectives:</span> {objectives.events.map(objectiveName).join(", ")}</p> : null}
        {window.metricOmissions.map((omission) => <p key={omission.kind} className="rounded-md bg-warning/10 px-3 py-2 text-xs text-warning">{metricLabels[omission.kind]} was omitted because the source counters were inconsistent.</p>)}
      </div>
    </div>
    {review.knowledge.coverage === "available" ? <KnowledgeContext annotations={window.knowledgeAnnotations} objectives={objectives?.kind === "eliteObjectiveContext" ? objectives.events : []} patch={review.knowledge.publicPatch} /> : null}
  </section>;
}

function EncounterChapters({ encounters, review, window, selectedEncounter, activeEvidenceId, onSelectEncounter, onSelectEvidence }: {
  encounters: Encounter[]; review: MatchReview; window: ReviewWindow; selectedEncounter: Encounter | null; activeEvidenceId: string | null;
  onSelectEncounter: (id: string | null) => void; onSelectEvidence: (id: string) => void;
}) {
  return <section aria-labelledby="encounters-heading" className="mt-6 border-t border-border/60 pt-5">
    <div className="flex flex-wrap items-end justify-between gap-2">
      <div><h4 id="encounters-heading" className="font-medium">Combat encounters</h4><p className="mt-1 text-xs text-muted-foreground">Distinct groups of recorded kills. Times show the first and last recorded kill, not the full fight duration.</p></div>
      <span className="text-xs tabular-nums text-muted-foreground">{formatCount(encounters.length, "encounter")}</span>
    </div>
    <div className="mt-4 flex flex-wrap items-center justify-between gap-3 rounded-md bg-muted/25 px-3 py-2">
      <span className="text-xs text-muted-foreground"><span className="font-medium text-foreground">Map scope</span> · All recorded evidence in this review window</span>
      <button type="button" aria-label="Window overview" aria-pressed={selectedEncounter === null} onClick={() => onSelectEncounter(null)}
        className={`rounded-md border px-3 py-1.5 text-xs font-medium transition-colors focus-visible:outline-2 focus-visible:outline-ring ${selectedEncounter === null ? "border-primary bg-primary/15 text-foreground ring-1 ring-primary/25" : "bg-card hover:bg-muted"}`}>Window overview</button>
    </div>
    {encounters.length === 0 ? <p className="mt-3 rounded-lg border border-dashed p-4 text-sm text-muted-foreground">No recorded champion kills in this period.</p> : (
      <ol className="mt-3 space-y-3">
        {encounters.map((encounter, index) => {
          const selected = selectedEncounter?.id === encounter.id;
          const player = encounter.configuredPlayerSummary;
          const names = encounter.participantIds.map((id) => review.participants.find((participant) => participant.participantId === id)?.championName ?? `Participant ${id}`);
          const eventEntries = selected ? buildSpatialEvidence(review, window, encounter).filter((entry) => entry.kind === "event") : [];
          return <li key={encounter.id} data-state={selected ? "selected" : "collapsed"} className={`overflow-hidden rounded-lg border transition-colors ${selected ? "border-primary bg-primary/5 ring-1 ring-primary/30" : "bg-background/25"}`}>
            <button type="button" aria-label={encounterLabel(encounter, index)} aria-pressed={selected} onClick={() => onSelectEncounter(encounter.id)} className="w-full px-4 py-3 text-left focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-ring">
              <span className="block font-medium tabular-nums">{encounterLabel(encounter, index)}</span>
              <span className="mt-1 block text-xs text-muted-foreground">{formatCount(encounter.combatEventCount, "kill event")} · {formatCount(encounter.distinctParticipantCount, "participant")} · {player.involved ? `You ${player.kills}/${player.deaths}/${player.assists} K/D/A` : "You not recorded in these kills"}</span>
              <span className="mt-1 block text-xs text-muted-foreground">{names.join(", ") || "No known participants"}</span>
            </button>
            {selected ? <EncounterEvents entries={eventEntries} activeEvidenceId={activeEvidenceId} onSelect={onSelectEvidence} /> : null}
          </li>;
        })}
      </ol>
    )}
  </section>;
}

function EncounterEvents({ entries, activeEvidenceId, onSelect }: { entries: SpatialEvidence[]; activeEvidenceId: string | null; onSelect: (id: string) => void }) {
  return <div className="border-t border-primary/20 bg-background/45 px-3 py-3">
    <h5 className="px-1 text-xs font-medium uppercase tracking-wider text-muted-foreground">Recorded events</h5>
    {entries.length === 0 ? <p className="px-1 pt-2 text-sm text-muted-foreground">No recorded events are available for this encounter.</p> : <ol aria-label="Encounter events" className="mt-2 space-y-1">
      {entries.map((entry) => <li key={entry.id}>
        <button type="button" aria-label={`${entry.label}, ${formatMatchTime(entry.timestampMs, true)}, ${spatialEvidenceLocationStatus(entry)}`} aria-pressed={activeEvidenceId === entry.id} onClick={() => onSelect(entry.id)}
          className={`flex w-full items-start gap-3 rounded-md px-3 py-2 text-left text-sm focus-visible:outline-2 focus-visible:outline-ring ${activeEvidenceId === entry.id ? "bg-primary/15" : "hover:bg-muted/50"}`}>
          <span className="w-20 shrink-0 tabular-nums text-muted-foreground">{formatMatchTime(entry.timestampMs, true)}</span>
          <span><span className="font-medium">{entry.label}</span><span className="block text-xs text-muted-foreground">{entry.layer === "objectives" ? "Nearby objective context" : "Recorded combat event"} · {spatialEvidenceLocationStatus(entry)}</span></span>
        </button>
      </li>)}
    </ol>}
    {entries.some((entry) => entry.layer === "objectives") ? <p className="mt-2 px-1 text-xs text-muted-foreground">Nearby objective events are time and location context; proximity does not establish causation or contestability.</p> : null}
  </div>;
}

function ScopeIndicator({ window, encounter, encounterIndex, evidence }: { window: ReviewWindow; encounter: Encounter | null; encounterIndex: number; evidence: SpatialEvidence | null }) {
  return <div className="mb-3 rounded-lg border bg-card/95 p-3 shadow-sm backdrop-blur lg:sticky lg:top-0 lg:z-20" aria-label="Current review scope">
    <p className="text-[11px] font-medium uppercase tracking-wider text-muted-foreground">Current scope</p>
    <div className="mt-2 flex flex-wrap items-center gap-x-2 gap-y-1 text-sm">
      <span className="font-medium tabular-nums">{formatMatchTime(window.requestedStartTimestampMs)}–{formatMatchTime(window.requestedEndTimestampMs)}</span>
      <span aria-hidden="true" className="text-muted-foreground">→</span>
      <span>{encounter ? encounterLabel(encounter, encounterIndex) : "Window overview"}</span>
      {evidence ? <><span aria-hidden="true" className="text-muted-foreground">→</span><span className="text-muted-foreground">{evidence.label} · {formatMatchTime(evidence.timestampMs, true)}</span></> : null}
    </div>
  </div>;
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

function MetricLine({ observation }: { observation: Extract<Observation, { kind: MetricKind }> }) {
  return <p className="flex flex-wrap justify-between gap-x-4 gap-y-1 text-sm"><span className="font-medium">{metricLabels[observation.kind]}:</span><span className="tabular-nums">{formatSigned(observation.relativeStart)} → {formatSigned(observation.relativeEnd)} ({formatSigned(observation.signedChange)})</span></p>;
}

function KnowledgeContext({ annotations, objectives, patch }: { annotations: KnowledgeAnnotation[]; objectives: ObjectiveEvent[]; patch: string | null }) {
  return <details className="mt-4 rounded-lg border bg-muted/10 p-3 text-muted-foreground">
    <summary className="cursor-pointer text-xs font-medium uppercase tracking-wider">Game context{patch ? ` · Patch ${patch}` : ""}</summary>
    {annotations.length === 0 ? <p className="mt-2 text-xs">No game-context annotations apply to this period.</p> : <div className="mt-3 space-y-3">{annotations.map((annotation, index) => {
      const recordedEvent = annotation.kind === "recordedObjectiveContext" ? objectives.find((event) => sameSource(event.source, annotation.target.event)) : undefined;
      if (annotation.kind === "nearInitialSpawn") return <p key={`${annotation.kind}-${annotation.fact.id}-${index}`} className="text-xs leading-5">Initial-spawn context · {knowledgeObjectiveName(annotation.fact.objective)} initial spawn: <span className="tabular-nums">{formatMatchTime(annotation.fact.initialSpawnTimestampMs)}</span></p>;
      return <div key={`${annotation.kind}-${annotation.fact.id}-${index}`} className="text-xs"><p className="font-medium text-foreground/80">Associated recorded kill: {recordedEvent ? objectiveName(recordedEvent) : knowledgeObjectiveName(annotation.fact.objective)} at {formatMatchTime(annotation.recordedKillTimestampMs)}</p><p className="mt-1">{knowledgeObjectiveName(annotation.fact.objective)} initial spawn: <span className="tabular-nums">{formatMatchTime(annotation.fact.initialSpawnTimestampMs)}</span></p></div>;
    })}</div>}
  </details>;
}

function EvidenceDisclosure({ window, metrics }: { window: ReviewWindow; metrics: Extract<Observation, { kind: MetricKind }>[] }) {
  const combat = window.observations.find((observation) => observation.kind === "configuredPlayerCombat");
  const objectives = window.observations.find((observation) => observation.kind === "eliteObjectiveContext");
  const sources = window.knowledgeAnnotations.flatMap((annotation) => annotation.fact.sources.map((source) => ({ ...source, factId: annotation.fact.id })));
  return <details className="mt-6 border-t pt-4 text-sm">
    <summary className="cursor-pointer font-medium">Evidence and sources</summary>
    <div className="mt-4 space-y-4 text-muted-foreground">
      <div className="space-y-1 tabular-nums"><p>Review bounds: {formatMatchTime(window.requestedStartTimestampMs, true)}–{formatMatchTime(window.requestedEndTimestampMs, true)}</p>{metrics.length > 0 || window.metricOmissions.length > 0 ? <p>Metric frames: {formatMatchTime(window.startFrame.timestampMs, true)} (frame {window.startFrame.frameIndex})–{formatMatchTime(window.endFrame.timestampMs, true)} (frame {window.endFrame.frameIndex})</p> : null}<p>Events after the start, through the end.</p></div>
      <div><p className="font-medium text-foreground">Detector selection information</p><p>Selection rank: {window.selectionRank}</p><p>Signal types: {window.signalKinds.length ? window.signalKinds.map((kind) => signalLabels[kind]).join(", ") : "None"}</p><p>Absorbed signal types: {window.absorbedSignalKinds.length ? window.absorbedSignalKinds.map((kind) => signalLabels[kind]).join(", ") : "None"}</p></div>
      {metrics.map((metric) => <div key={metric.kind}><p className="font-medium text-foreground">{metricLabels[metric.kind]} absolute endpoints</p><p>Configured player: {formatNumber(metric.configuredPlayer.startValue)} → {formatNumber(metric.configuredPlayer.endValue)}</p><p>Enemy jungler: {formatNumber(metric.enemyJungler.startValue)} → {formatNumber(metric.enemyJungler.endValue)}</p></div>)}
      {combat?.kind === "configuredPlayerCombat" && combat.events.length ? <div><p className="font-medium text-foreground">Combat event references</p>{combat.events.map((event) => <p key={`${event.source.frameIndex}-${event.source.eventIndex}`}>{formatMatchTime(event.timestampMs, true)} · frame {event.source.frameIndex}, event {event.source.eventIndex}</p>)}</div> : null}
      {objectives?.kind === "eliteObjectiveContext" && objectives.events.length ? <div><p className="font-medium text-foreground">Objective event references</p>{objectives.events.map((event) => <p key={`${event.source.frameIndex}-${event.source.eventIndex}`}>{objectiveName(event)} · {formatMatchTime(event.timestampMs, true)} · frame {event.source.frameIndex}, event {event.source.eventIndex} · attribution {event.teamAttribution.kind}</p>)}</div> : null}
      {sources.length ? <div><p className="font-medium text-foreground">Mechanic sources</p><ul className="mt-1 list-disc space-y-1 pl-5">{sources.map((source, index) => <li key={`${source.factId}-${source.url}-${index}`}><a className="text-primary underline underline-offset-4" href={source.url} target="_blank" rel="noreferrer">{source.title}</a></li>)}</ul></div> : null}
    </div>
  </details>;
}

function orderedEncounters(window: ReviewWindow | undefined): Encounter[] {
  return window ? [...window.encounters].sort((left, right) => left.startTimestampMs - right.startTimestampMs || left.endTimestampMs - right.endTimestampMs || left.id.localeCompare(right.id)) : [];
}

function timelineLanes(windows: ReviewWindow[]): number[] {
  const laneEnds: number[] = [];
  return windows.map((window) => {
    let lane = laneEnds.findIndex((end) => window.requestedStartTimestampMs >= end);
    if (lane < 0) lane = laneEnds.length;
    laneEnds[lane] = window.requestedEndTimestampMs;
    return lane;
  });
}

function encounterLabel(encounter: Encounter, index: number): string {
  const time = encounter.combatEventCount === 1 || encounter.startTimestampMs === encounter.endTimestampMs
    ? formatMatchTime(encounter.startTimestampMs, true)
    : `${formatMatchTime(encounter.startTimestampMs, true)}–${formatMatchTime(encounter.endTimestampMs, true)}`;
  return `Encounter ${index + 1} · ${time}`;
}

function windowKey(window: ReviewWindow): string { return `${window.requestedStartTimestampMs}-${window.requestedEndTimestampMs}-${window.selectionRank}`; }
function isMetricObservation(observation: Observation): observation is Extract<Observation, { kind: MetricKind }> { return observation.kind === "relativeGoldMovement" || observation.kind === "relativeXpMovement" || observation.kind === "relativeJungleCsMovement"; }
function sameSource(left: SourceEvent, right: SourceEvent) { return left.frameIndex === right.frameIndex && left.eventIndex === right.eventIndex; }
export function formatMatchTime(timestampMs: number, exact = false) { const floored = Math.floor(timestampMs); const totalSeconds = Math.floor(floored / 1000); const minutes = Math.floor(totalSeconds / 60); const seconds = String(totalSeconds % 60).padStart(2, "0"); return exact ? `${minutes}:${seconds}.${String(floored % 1000).padStart(3, "0")}` : `${minutes}:${seconds}`; }
export function formatSigned(value: number) { return value > 0 ? `+${formatNumber(value)}` : value < 0 ? `−${formatNumber(Math.abs(value))}` : "0"; }
function formatNumber(value: number) { return new Intl.NumberFormat("en-US").format(value); }
function formatCount(value: number, singular: string) { return `${value} ${singular}${value === 1 ? "" : "s"}`; }
function knowledgeObjectiveName(objective: "elementalDragon" | "baronNashor") { return objective === "elementalDragon" ? "Elemental Dragon" : "Baron Nashor"; }
export function objectiveName(event: ObjectiveEvent) { if (event.monsterType === "BARON_NASHOR") return "Baron Nashor"; const subtype = event.monsterSubType ?? event.monsterType ?? "Unknown objective"; const names: Record<string, string> = { AIR_DRAGON: "Cloud Dragon", EARTH_DRAGON: "Mountain Dragon", FIRE_DRAGON: "Infernal Dragon", WATER_DRAGON: "Ocean Dragon", HEXTECH_DRAGON: "Hextech Dragon", CHEMTECH_DRAGON: "Chemtech Dragon", ELDER_DRAGON: "Elder Dragon" }; return names[subtype] ?? subtype; }
