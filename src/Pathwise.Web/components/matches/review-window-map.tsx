"use client";

import Image from "next/image";
import { useState } from "react";
import type { Encounter, MatchReview, ReviewWindow } from "@/lib/api/pathwise";
import { projectSummonersRiftPosition } from "@/lib/maps/summoners-rift-map-v1-projection";
import { buildSpatialEvidence, spatialEvidenceLocationStatus, type EvidenceLayer, type SpatialEvidence } from "./spatial-evidence";

const layerLabels: Record<EvidenceLayer, string> = { you: "You", enemy: "Enemy jungler", combat: "Combat", objectives: "Objectives" };
const layers: EvidenceLayer[] = ["you", "enemy", "combat", "objectives"];

function time(timestampMs: number, exact = false): string {
  const minutes = Math.floor(timestampMs / 60_000);
  const seconds = String(Math.floor(timestampMs / 1000) % 60).padStart(2, "0");
  return `${minutes}:${seconds}${exact ? `.${String(timestampMs % 1000).padStart(3, "0")}` : ""}`;
}

function markerClass(layer: EvidenceLayer): string {
  switch (layer) {
    case "you": return "rounded-full border-[3px] border-white bg-emerald-500 text-white";
    case "enemy": return "rounded-full border-[3px] border-amber-300 bg-slate-900 text-amber-100";
    case "combat": return "rounded-t-full rounded-b-sm border-2 border-white bg-rose-600 text-white";
    case "objectives": return "text-white";
  }
}

export function ReviewWindowMap({ review, window, encounter, enabled, activeId, onToggle, onActiveChange }: {
  review: MatchReview;
  window: ReviewWindow;
  encounter: Encounter | null;
  enabled: Record<EvidenceLayer, boolean>;
  activeId: string | null;
  onToggle: (layer: EvidenceLayer) => void;
  onActiveChange: (id: string) => void;
}) {
  const [assetFailed, setAssetFailed] = useState(false);
  const entries = buildSpatialEvidence(review, window, encounter).filter((entry) => enabled[entry.layer]);
  const active = entries.find((entry) => entry.id === activeId) ?? null;
  const samples = entries.filter((entry) => entry.kind === "sample");
  const supported = review.mapId === 11;
  const detailId = `spatial-detail-${window.requestedStartTimestampMs}-${window.requestedEndTimestampMs}`;

  return <section aria-label="Map evidence" className="rounded-lg border bg-background/25 p-3 sm:p-4">
    <div>
      <h4 className="font-medium">Map evidence</h4>
      <p className="mt-1 text-xs leading-5 text-muted-foreground">Recorded events and periodic samples only. Movement between samples is unknown.</p>
    </div>
    <fieldset className="mt-3 flex flex-wrap gap-2" aria-label="Map layers">
      <legend className="sr-only">Map layers</legend>
      {layers.map((layer) => {
        const unavailable = layer === "enemy" && review.enemyResolution.status !== "resolved";
        return <label key={layer} className="inline-flex min-h-9 items-center gap-2 rounded-md border px-2.5 text-xs focus-within:ring-2 focus-within:ring-ring">
          <input type="checkbox" checked={enabled[layer] && !unavailable} disabled={unavailable} onChange={() => onToggle(layer)} className="accent-emerald-500" />
          <span>{layerLabels[layer]}</span>
        </label>;
      })}
    </fieldset>
    {review.enemyResolution.status !== "resolved" ? <p className="mt-2 text-xs text-muted-foreground">Enemy jungler positions are unavailable because the opposing jungler was {review.enemyResolution.status === "missing" ? "not identified" : "identified ambiguously"}.</p> : null}
    {encounter && samples.length === 0 ? <p className="mt-2 text-xs text-muted-foreground">No nearby frame samples are shown for this encounter.</p> : null}
    <div className="mt-3">
      {!supported ? <p className="rounded-md border p-4 text-sm">Summoner&apos;s Rift artwork is unavailable for map {review.mapId}. Recorded evidence remains available below.</p> : assetFailed ? <p role="alert" className="rounded-md border p-4 text-sm">Map artwork could not load. Recorded evidence remains available below.</p> : (
        <div className="relative mx-auto aspect-square w-full max-w-80 overflow-hidden rounded-md border bg-slate-950" aria-label="Summoner's Rift reference map">
          <Image src="/maps/riot/16.18.1/map11.png" alt="Static Summoner's Rift reference artwork" fill sizes="(min-width: 1280px) 35vw, (min-width: 1024px) 42vw, 100vw" className="object-fill" onError={() => setAssetFailed(true)} />
          {entries.map((entry) => {
            const point = entry.position && projectSummonersRiftPosition(entry.position);
            if (!point) return null;
            return <button key={entry.id} type="button" aria-label={`${entry.label}, ${time(entry.timestampMs, true)}, ${entry.kind === "sample" ? "frame sample" : "recorded event"}`}
              aria-pressed={activeId === entry.id} aria-controls={detailId} onClick={() => onActiveChange(entry.id)}
              className={`absolute flex size-7 -translate-x-1/2 -translate-y-1/2 items-center justify-center text-[11px] font-bold shadow-md focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-white ${markerClass(entry.layer)} ${activeId === entry.id ? "ring-2 ring-white ring-offset-2 ring-offset-slate-950" : ""}`}
              style={{ left: `${point.xPercent}%`, top: `${point.yPercent}%`, zIndex: activeId === entry.id ? 20 : 10 }}>
              {entry.layer === "objectives" ? <span aria-hidden="true" className="absolute inset-0 rotate-45 border-2 border-white bg-sky-500" /> : null}
              <span className="relative">{entry.layer === "you" ? "Y" : entry.layer === "enemy" ? "E" : entry.layer === "combat" ? "!" : "◆"}</span>
              {activeId === entry.id ? <span aria-hidden="true" className={`absolute whitespace-nowrap rounded bg-slate-950 px-1.5 py-0.5 text-xs text-white ${point.xPercent > 65 ? "right-full mr-2" : "left-full ml-2"}`}>{time(entry.timestampMs, true)}</span> : null}
            </button>;
          })}
        </div>
      )}
      <p className="mt-2 text-[11px] text-muted-foreground">Y/E circles are frame samples; ! and ◆ are event-reported positions. No path is inferred.</p>
    </div>
    <SpatialEvidenceDetails entry={active} detailId={detailId} windowStartMs={window.requestedStartTimestampMs} />
    <SampleDisclosure entries={samples} activeId={activeId} onSelect={onActiveChange} detailId={detailId} windowStartMs={window.requestedStartTimestampMs} />
    <details className="mt-3 border-t pt-3 text-[11px] text-muted-foreground">
      <summary className="cursor-pointer font-medium">Map notes and attribution</summary>
      <p className="mt-2">Static reference artwork; not reconstructed terrain or objective availability. Map artwork © Riot Games. Pathwise is not endorsed by Riot Games and does not reflect the views or opinions of Riot Games or anyone officially involved in producing or managing Riot Games properties. Riot Games and all associated properties are trademarks or registered trademarks of Riot Games, Inc. <a href="https://developer.riotgames.com/docs/lol#data-dragon_other" className="underline" target="_blank" rel="noreferrer">Asset source</a> · <a href="https://www.riotgames.com/en/legal" className="underline" target="_blank" rel="noreferrer">Riot legal</a> · <a href="https://developer.riotgames.com/terms" className="underline" target="_blank" rel="noreferrer">API terms</a>.</p>
    </details>
  </section>;
}

function SampleDisclosure({ entries, activeId, onSelect, detailId, windowStartMs }: {
  entries: SpatialEvidence[]; activeId: string | null; onSelect: (id: string) => void; detailId: string; windowStartMs: number;
}) {
  const sampleTimes = [...new Set(entries.map((entry) => entry.timestampMs))];
  return <details className="mt-3 rounded-md border bg-muted/10 text-sm">
    <summary className="cursor-pointer px-3 py-2 font-medium">Nearby frame samples ({entries.length})</summary>
    {entries.length === 0 ? <p className="border-t p-3 text-sm text-muted-foreground">No frame samples are visible with these layers.</p> : <ol className="border-t">
      {entries.map((entry, index) => {
        const sampleIndex = sampleTimes.indexOf(entry.timestampMs);
        const firstInFrame = index === 0 || entries[index - 1].timestampMs !== entry.timestampMs;
        const gap = firstInFrame && sampleIndex > 0 ? entry.timestampMs - sampleTimes[sampleIndex - 1] : null;
        return <li key={entry.id} className="border-b border-border/50 last:border-b-0">
          {gap !== null ? <p className="px-3 pt-2 text-xs text-muted-foreground">{time(gap, true)} between samples; no movement is inferred.</p> : null}
          {firstInFrame ? <p className="px-3 pt-2 text-xs font-medium text-muted-foreground">One periodic snapshot · {time(entry.timestampMs, true)}</p> : null}
          <button type="button" aria-pressed={activeId === entry.id} aria-controls={detailId} onClick={() => onSelect(entry.id)}
            className={`flex min-h-11 w-full items-start gap-3 px-3 py-2 text-left text-sm focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-ring ${activeId === entry.id ? "bg-primary/15" : "hover:bg-muted/40"}`}>
            <span className="w-20 shrink-0 tabular-nums text-muted-foreground">{time(entry.timestampMs, true)}</span>
            <span><span className="font-medium">{entry.label}</span><span className="block text-xs text-muted-foreground">{entry.boundary ? `Before this period · starts ${time(windowStartMs, true)}` : "Periodic frame sample"} · {spatialEvidenceLocationStatus(entry)}</span></span>
          </button>
        </li>;
      })}
    </ol>}
  </details>;
}

export function SpatialEvidenceDetails({ entry, detailId, windowStartMs }: { entry: SpatialEvidence | null; detailId: string; windowStartMs: number }) {
  return <div id={detailId} aria-live="polite" className="mt-3 min-h-24 rounded-md border bg-muted/20 p-3 text-sm">
    {entry ? <>
      <h5 className="font-medium">{entry.label} · {time(entry.timestampMs, true)}</h5>
      <p className="mt-2 text-muted-foreground">{entry.description}</p>
      <p className="mt-1 text-muted-foreground">{spatialEvidenceLocationStatus(entry)}</p>
      {entry.boundary ? <p className="mt-2 text-muted-foreground">Before this period: sampled at {time(entry.timestampMs, true)}. The period starts at {time(windowStartMs, true)}.</p> : null}
    </> : <p className="text-muted-foreground">No evidence is visible with these layers.</p>}
  </div>;
}
