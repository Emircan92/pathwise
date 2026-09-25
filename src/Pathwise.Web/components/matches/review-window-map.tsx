"use client";

import Image from "next/image";
import { useState } from "react";
import type { Encounter, MatchReview, ReviewWindow } from "@/lib/api/pathwise";
import { projectSummonersRiftPosition } from "@/lib/maps/summoners-rift-map-v1-projection";
import { buildSpatialEvidence, initialSpatialEvidence, type EvidenceLayer, type SpatialEvidence } from "./spatial-evidence";

const layerLabels: Record<EvidenceLayer, string> = { you: "You", enemy: "Enemy jungler", combat: "Combat", objectives: "Objectives" };
const layers: EvidenceLayer[] = ["you", "enemy", "combat", "objectives"];

function time(timestampMs: number, exact = false): string {
  const minutes = Math.floor(timestampMs / 60_000);
  const seconds = String(Math.floor(timestampMs / 1000) % 60).padStart(2, "0");
  return `${minutes}:${seconds}${exact ? `.${String(timestampMs % 1000).padStart(3, "0")}` : ""}`;
}

function locationStatus(entry: SpatialEvidence): string {
  if (entry.position === null) return "Location not recorded";
  return projectSummonersRiftPosition(entry.position) === null ? "Outside calibrated map" : "Recorded location on map";
}

function markerClass(layer: EvidenceLayer): string {
  switch (layer) {
    case "you": return "rounded-full border-[3px] border-white bg-emerald-500 text-white";
    case "enemy": return "rounded-full border-[3px] border-amber-300 bg-slate-900 text-amber-100";
    case "combat": return "rounded-t-full rounded-b-sm border-2 border-white bg-rose-600 text-white";
    case "objectives": return "text-white";
  }
}

export function ReviewWindowMap({ review, window, encounter, enabled, onToggle }: {
  review: MatchReview;
  window: ReviewWindow;
  encounter: Encounter | null;
  enabled: Record<EvidenceLayer, boolean>;
  onToggle: (layer: EvidenceLayer) => void;
}) {
  const [activeId, setActiveId] = useState<string | null>(() => initialSpatialEvidence(buildSpatialEvidence(review, window, encounter).filter((entry) => enabled[entry.layer]), encounter !== null));
  const [assetFailed, setAssetFailed] = useState(false);
  const entries = buildSpatialEvidence(review, window, encounter).filter((entry) => enabled[entry.layer]);
  const active = entries.find((entry) => entry.id === activeId) ?? null;
  const supported = review.mapId === 11;
  const detailId = `spatial-detail-${window.requestedStartTimestampMs}-${window.requestedEndTimestampMs}`;

  function toggle(layer: EvidenceLayer) {
    if (enabled[layer] && active?.layer === layer) setActiveId(null);
    onToggle(layer);
  }

  return <section aria-label="Spatial evidence" className="mt-6 border-t border-border/60 pt-5">
    <h4 className="font-medium">Positions and recorded events</h4>
    <p className="mt-2 text-sm leading-6 text-muted-foreground">Positions are periodic snapshots. Events use their recorded time and location. Movement between snapshots is unknown.</p>
    <p className="mt-1 text-xs text-muted-foreground">Background: static reference artwork, not reconstructed terrain or objective availability.</p>
    <fieldset className="mt-4 flex flex-wrap gap-2" aria-label="Map layers">
      <legend className="sr-only">Map layers</legend>
      {layers.map((layer) => {
        const unavailable = layer === "enemy" && review.enemyResolution.status !== "resolved";
        return <label key={layer} className="inline-flex min-h-11 items-center gap-2 rounded-md border px-3 text-sm focus-within:ring-2 focus-within:ring-ring">
          <input type="checkbox" checked={enabled[layer] && !unavailable} disabled={unavailable} onChange={() => toggle(layer)} className="accent-emerald-500" />
          <span>{layerLabels[layer]}</span>
        </label>;
      })}
    </fieldset>
    {review.enemyResolution.status !== "resolved" ? <p className="mt-2 text-xs text-muted-foreground">Enemy jungler positions are unavailable because the opposing jungler was {review.enemyResolution.status === "missing" ? "not identified" : "identified ambiguously"}.</p> : null}
    {encounter && !entries.some((entry) => entry.kind === "sample") ? <p className="mt-2 text-xs text-muted-foreground">No nearby frame samples fall within this encounter&apos;s ±30-second context range.</p> : null}
    <div className="mt-4 grid gap-5 lg:grid-cols-[minmax(0,1fr)_minmax(18rem,0.85fr)]">
      <div>
        {!supported ? <p className="rounded-md border p-4 text-sm">Summoner&apos;s Rift artwork is unavailable for map {review.mapId}. Recorded evidence remains below.</p> : assetFailed ? <p role="alert" className="rounded-md border p-4 text-sm">Map artwork could not load. Recorded evidence remains available in the timeline.</p> : (
          <div className="relative aspect-square w-full overflow-hidden rounded-md border bg-slate-950" aria-label="Summoner's Rift reference map">
            <Image src="/maps/riot/16.18.1/map11.png" alt="Static Summoner's Rift reference artwork" fill sizes="(min-width: 1024px) 50vw, 100vw" className="object-fill" onError={() => setAssetFailed(true)} />
            {entries.map((entry) => {
              const point = entry.position && projectSummonersRiftPosition(entry.position);
              if (!point) return null;
              return <button key={entry.id} type="button" aria-label={`${entry.label}, ${time(entry.timestampMs, true)}, ${entry.kind === "sample" ? "frame sample" : "recorded event"}`}
                aria-pressed={activeId === entry.id} aria-controls={detailId}
                onClick={() => setActiveId(entry.id)} onFocus={() => setActiveId(entry.id)}
                className={`absolute flex size-7 -translate-x-1/2 -translate-y-1/2 items-center justify-center text-[11px] font-bold shadow-md focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-white ${markerClass(entry.layer)} ${activeId === entry.id ? "ring-2 ring-white ring-offset-2 ring-offset-slate-950" : ""}`}
                style={{ left: `${point.xPercent}%`, top: `${point.yPercent}%`, zIndex: activeId === entry.id ? 20 : 10 }}>
                {entry.layer === "objectives" ? <span aria-hidden="true" className="absolute inset-0 rotate-45 border-2 border-white bg-sky-500" /> : null}
                <span className="relative">{entry.layer === "you" ? "Y" : entry.layer === "enemy" ? "E" : entry.layer === "combat" ? "!" : "◆"}</span>
                {activeId === entry.id ? <span aria-hidden="true" className={`absolute whitespace-nowrap rounded bg-slate-950 px-1.5 py-0.5 text-xs text-white ${point.xPercent > 65 ? "right-full mr-2" : "left-full ml-2"}`}>{time(entry.timestampMs, true)}</span> : null}
              </button>;
            })}
          </div>
        )}
        <p className="mt-2 text-xs text-muted-foreground">Legend: filled circle Y = you; outlined circle E = enemy jungler; pin ! = combat; diamond ◆ = objective.</p>
        <p className="mt-2 text-[11px] text-muted-foreground">Map artwork © Riot Games. Pathwise is not endorsed by Riot Games and does not reflect the views or opinions of Riot Games or anyone officially involved in producing or managing Riot Games properties. Riot Games and all associated properties are trademarks or registered trademarks of Riot Games, Inc. <a href="https://developer.riotgames.com/docs/lol#data-dragon_other" className="underline" target="_blank" rel="noreferrer">Asset source</a> · <a href="https://www.riotgames.com/en/legal" className="underline" target="_blank" rel="noreferrer">Riot legal</a> · <a href="https://developer.riotgames.com/terms" className="underline" target="_blank" rel="noreferrer">API terms</a>.</p>
      </div>
      <div className="min-w-0">
        <SpatialEvidenceRail entries={entries} activeId={activeId} onSelect={setActiveId} detailId={detailId} windowStartMs={window.requestedStartTimestampMs} />
        <SpatialEvidenceDetails entry={active} detailId={detailId} windowStartMs={window.requestedStartTimestampMs} />
      </div>
    </div>
  </section>;
}

export function SpatialEvidenceRail({ entries, activeId, onSelect, detailId, windowStartMs }: {
  entries: SpatialEvidence[]; activeId: string | null; onSelect: (id: string) => void; detailId: string; windowStartMs: number;
}) {
  const sampleTimes = [...new Set(entries.filter((entry) => entry.kind === "sample").map((entry) => entry.timestampMs))];
  return <div aria-label="Chronological evidence" className="max-h-[28rem] overflow-y-auto rounded-md border bg-background/30">
    <h5 className="sticky top-0 z-10 border-b bg-card px-3 py-2 text-sm font-medium">Chronological evidence</h5>
    {entries.length === 0 ? <p className="p-3 text-sm text-muted-foreground">No evidence is visible with these layers.</p> : <ol>
      {entries.map((entry) => {
        const sampleIndex = entry.kind === "sample" ? sampleTimes.indexOf(entry.timestampMs) : -1;
        const isFirstSampleInFrame = entry.kind === "sample" && !entries.slice(0, entries.indexOf(entry)).some((prior) => prior.kind === "sample" && prior.timestampMs === entry.timestampMs);
        const gap = isFirstSampleInFrame && sampleIndex > 0 ? entry.timestampMs - sampleTimes[sampleIndex - 1] : null;
        return <li key={entry.id} className="border-b border-border/50 last:border-b-0">
          {gap !== null ? <p className="px-3 pt-2 text-xs text-muted-foreground">{time(gap, true)} between sampled frames; event pins do not fill position gaps.</p> : null}
          {isFirstSampleInFrame ? <p className="px-3 pt-2 text-xs font-medium text-muted-foreground">Together in one snapshot · {time(entry.timestampMs, true)}</p> : null}
          <button type="button" aria-pressed={activeId === entry.id} aria-controls={detailId} onClick={() => onSelect(entry.id)} onFocus={() => onSelect(entry.id)}
            className={`flex min-h-12 w-full items-start gap-3 px-3 py-2 text-left text-sm focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-ring ${activeId === entry.id ? "bg-primary/15" : "hover:bg-muted/40"}`}>
            <span className="w-20 shrink-0 tabular-nums text-muted-foreground">{time(entry.timestampMs, true)}</span>
            <span><span className="font-medium">{entry.label}</span><span className="block text-xs text-muted-foreground">{entry.boundary ? `Before this period · starts ${time(windowStartMs, true)}` : entry.kind === "sample" ? "Frame sample" : "Recorded event"} · {locationStatus(entry)}</span></span>
          </button>
        </li>;
      })}
    </ol>}
  </div>;
}

export function SpatialEvidenceDetails({ entry, detailId, windowStartMs }: { entry: SpatialEvidence | null; detailId: string; windowStartMs: number }) {
  return <div id={detailId} aria-live="polite" className="mt-3 min-h-28 rounded-md border bg-muted/20 p-4 text-sm">
    {entry ? <>
      <h5 className="font-medium">{entry.label} · {time(entry.timestampMs, true)}</h5>
      <p className="mt-2 text-muted-foreground">{entry.description}</p>
      <p className="mt-1 text-muted-foreground">{locationStatus(entry)}</p>
      {entry.boundary ? <p className="mt-2 text-muted-foreground">Before this period: sampled at {time(entry.timestampMs, true)}. The period starts at {time(windowStartMs, true)}.</p> : null}
    </> : <p className="text-muted-foreground">Select an evidence item for details.</p>}
  </div>;
}
