"use client";

import Image from "next/image";
import { Focus, Maximize2, Minimize2, Minus, Plus, RotateCcw } from "lucide-react";
import { useEffect, useRef, useState, type KeyboardEvent, type PointerEvent, type WheelEvent } from "react";
import { createPortal } from "react-dom";
import { Button } from "@/components/ui/button";
import type { Encounter, MatchReview, ReviewWindow } from "@/lib/api/pathwise";
import { projectSummonersRiftPosition } from "@/lib/maps/summoners-rift-map-v1-projection";
import {
  FIT_MAP_CAMERA,
  MAP_ZOOM_STEP,
  clampMapCamera,
  focusMapCamera,
  panMapCamera,
  zoomMapCameraAt,
  type MapCamera,
} from "./map-camera";
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
  const [expanded, setExpanded] = useState(false);
  const scopeKey = `${window.requestedStartTimestampMs}-${window.requestedEndTimestampMs}:${encounter?.id ?? "overview"}`;
  const [cameraState, setCameraState] = useState<{ scopeKey: string; camera: MapCamera }>({ scopeKey, camera: FIT_MAP_CAMERA });
  const camera = cameraState.scopeKey === scopeKey ? cameraState.camera : FIT_MAP_CAMERA;
  const viewportRef = useRef<HTMLDivElement>(null);
  const dragRef = useRef<{ pointerId: number; startX: number; startY: number; camera: MapCamera } | null>(null);
  const entries = buildSpatialEvidence(review, window, encounter).filter((entry) => enabled[entry.layer]);
  const active = entries.find((entry) => entry.id === activeId) ?? null;
  const activePoint = active?.position ? projectSummonersRiftPosition(active.position) : null;
  const samples = entries.filter((entry) => entry.kind === "sample");
  const supported = review.mapId === 11;
  const detailId = `spatial-detail-${window.requestedStartTimestampMs}-${window.requestedEndTimestampMs}`;
  const zoomPercent = Math.round(camera.zoom * 100);

  function viewportSize() {
    const rectangle = viewportRef.current?.getBoundingClientRect();
    return { width: rectangle?.width || 512, height: rectangle?.height || 512 };
  }

  function updateCamera(update: (current: MapCamera) => MapCamera) {
    setCameraState((current) => ({
      scopeKey,
      camera: update(current.scopeKey === scopeKey ? current.camera : FIT_MAP_CAMERA),
    }));
  }

  function zoomAtCenter(nextZoom: number) {
    const viewport = viewportSize();
    updateCamera((current) => zoomMapCameraAt(current, nextZoom, { x: viewport.width / 2, y: viewport.height / 2 }, viewport));
  }

  function fitMap() {
    setCameraState({ scopeKey, camera: FIT_MAP_CAMERA });
  }

  function focusSelected() {
    if (!activePoint) return;
    updateCamera(() => focusMapCamera(activePoint, viewportSize()));
  }

  function handleWheel(event: WheelEvent<HTMLDivElement>) {
    event.preventDefault();
    const rectangle = event.currentTarget.getBoundingClientRect();
    const viewport = { width: rectangle.width || 512, height: rectangle.height || 512 };
    const amount = Math.max(-0.5, Math.min(0.5, -event.deltaY * 0.002));
    updateCamera((current) => zoomMapCameraAt(current, current.zoom + amount, {
      x: event.clientX - rectangle.left,
      y: event.clientY - rectangle.top,
    }, viewport));
  }

  function handlePointerDown(event: PointerEvent<HTMLDivElement>) {
    if (camera.zoom === 1 || event.button !== 0 || (event.target as Element).closest("button")) return;
    dragRef.current = { pointerId: event.pointerId, startX: event.clientX, startY: event.clientY, camera };
    event.currentTarget.setPointerCapture?.(event.pointerId);
  }

  function handlePointerMove(event: PointerEvent<HTMLDivElement>) {
    const drag = dragRef.current;
    if (!drag || drag.pointerId !== event.pointerId) return;
    updateCamera(() => panMapCamera(drag.camera, { x: event.clientX - drag.startX, y: event.clientY - drag.startY }, viewportSize()));
  }

  function finishPointerInteraction(event: PointerEvent<HTMLDivElement>) {
    if (dragRef.current?.pointerId !== event.pointerId) return;
    dragRef.current = null;
    if (event.currentTarget.hasPointerCapture?.(event.pointerId)) event.currentTarget.releasePointerCapture(event.pointerId);
  }

  function handleMapKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    const movement: Record<string, { x: number; y: number }> = {
      ArrowLeft: { x: 48, y: 0 }, ArrowRight: { x: -48, y: 0 }, ArrowUp: { x: 0, y: 48 }, ArrowDown: { x: 0, y: -48 },
    };
    if (movement[event.key] && camera.zoom > 1) {
      event.preventDefault();
      updateCamera((current) => panMapCamera(current, movement[event.key], viewportSize()));
    } else if (event.key === "+" || event.key === "=") {
      event.preventDefault();
      zoomAtCenter(camera.zoom + MAP_ZOOM_STEP);
    } else if (event.key === "-") {
      event.preventDefault();
      zoomAtCenter(camera.zoom - MAP_ZOOM_STEP);
    } else if (event.key === "Home") {
      event.preventDefault();
      fitMap();
    }
  }

  useEffect(() => {
    function handleResize() {
      setCameraState((current) => current.scopeKey === scopeKey
        ? { scopeKey, camera: clampMapCamera(current.camera, viewportSize()) }
        : current);
    }
    globalThis.window.addEventListener("resize", handleResize);
    return () => globalThis.window.removeEventListener("resize", handleResize);
  }, [scopeKey]);

  useEffect(() => {
    if (!expanded) return;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    function handleEscape(event: globalThis.KeyboardEvent) {
      if (event.key === "Escape") setExpanded(false);
    }
    globalThis.window.addEventListener("keydown", handleEscape);
    return () => {
      document.body.style.overflow = previousOverflow;
      globalThis.window.removeEventListener("keydown", handleEscape);
    };
  }, [expanded]);

  const content = <section aria-label="Map evidence" data-focus-mode={expanded ? "expanded" : "inline"}
      className={expanded ? "fixed inset-3 z-50 overflow-y-auto rounded-xl border bg-background p-4 shadow-2xl sm:inset-6 sm:p-6" : "rounded-lg border bg-background/25 p-3 sm:p-4"}>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h4 className="font-medium">Map evidence</h4>
          <p className="mt-1 text-xs leading-5 text-muted-foreground">Recorded events and periodic samples only. Movement between samples is unknown.</p>
          {expanded ? <p className="mt-1 text-xs tabular-nums text-muted-foreground">{encounter ? `Encounter scope · ${time(encounter.startTimestampMs, true)}–${time(encounter.endTimestampMs, true)}` : `Window overview · ${time(window.requestedStartTimestampMs)}–${time(window.requestedEndTimestampMs)}`}{active ? ` · ${active.label} at ${time(active.timestampMs, true)}` : ""}</p> : null}
        </div>
        <Button type="button" variant="outline" size="sm" autoFocus={expanded} aria-label={expanded ? "Exit map focus mode" : "Open map focus mode"} aria-pressed={expanded} onClick={() => setExpanded((current) => !current)}>
          {expanded ? <Minimize2 aria-hidden="true" /> : <Maximize2 aria-hidden="true" />}{expanded ? "Exit focus" : "Focus mode"}
        </Button>
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
        {supported && !assetFailed ? <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
          <div className="flex items-center gap-1" aria-label="Map camera controls">
            <Button type="button" variant="outline" size="icon-sm" aria-label="Zoom out" disabled={camera.zoom <= 1} onClick={() => zoomAtCenter(camera.zoom - MAP_ZOOM_STEP)}><Minus aria-hidden="true" /></Button>
            <span role="status" aria-label="Map zoom" className="min-w-12 text-center text-xs tabular-nums text-muted-foreground">{zoomPercent}%</span>
            <Button type="button" variant="outline" size="icon-sm" aria-label="Zoom in" disabled={camera.zoom >= 4} onClick={() => zoomAtCenter(camera.zoom + MAP_ZOOM_STEP)}><Plus aria-hidden="true" /></Button>
            <Button type="button" variant="ghost" size="sm" aria-label="Fit full map" disabled={camera.zoom === 1 && camera.panX === 0 && camera.panY === 0} onClick={fitMap}><RotateCcw aria-hidden="true" />Fit</Button>
          </div>
          <Button type="button" variant="ghost" size="sm" disabled={!activePoint} onClick={focusSelected} aria-label="Focus selected evidence"><Focus aria-hidden="true" />Focus selected</Button>
        </div> : null}
        {!supported ? <p className="rounded-md border p-4 text-sm">Summoner&apos;s Rift artwork is unavailable for map {review.mapId}. Recorded evidence remains available below.</p> : assetFailed ? <p role="alert" className="rounded-md border p-4 text-sm">Map artwork could not load. Recorded evidence remains available below.</p> : (
          <div ref={viewportRef} tabIndex={0} aria-label="Interactive Summoner's Rift map" aria-describedby={`${detailId}-camera-help`}
            data-zoom={camera.zoom.toFixed(2)} data-pan-x={Math.round(camera.panX)} data-pan-y={Math.round(camera.panY)}
            className={`relative mx-auto aspect-square w-full touch-none select-none overflow-hidden rounded-md border bg-slate-950 outline-none focus-visible:ring-2 focus-visible:ring-ring ${camera.zoom > 1 ? "cursor-grab active:cursor-grabbing" : ""}`}
            style={{ maxWidth: expanded ? "min(100%, calc(100vh - 13rem))" : undefined }}
            onWheel={handleWheel} onPointerDown={handlePointerDown} onPointerMove={handlePointerMove} onPointerUp={finishPointerInteraction} onPointerCancel={finishPointerInteraction} onKeyDown={handleMapKeyDown}>
            <div className="absolute inset-0 will-change-transform" style={{ transform: `translate3d(${camera.panX}px, ${camera.panY}px, 0) scale(${camera.zoom})`, transformOrigin: "50% 50%" }}>
              <Image src="/maps/riot/16.18.1/map11.png" alt="Static Summoner's Rift reference artwork" fill draggable={false} sizes="(min-width: 1280px) 45vw, (min-width: 1024px) 50vw, 100vw" className="pointer-events-none object-fill" onError={() => setAssetFailed(true)} />
              {entries.map((entry) => {
                const point = entry.position && projectSummonersRiftPosition(entry.position);
                if (!point) return null;
                const selected = activeId === entry.id;
                return <button key={entry.id} type="button" aria-label={`${entry.label}, ${time(entry.timestampMs, true)}, ${entry.kind === "sample" ? "frame sample" : "recorded event"}`}
                  aria-pressed={selected} aria-controls={detailId} onClick={() => onActiveChange(entry.id)}
                  className={`absolute flex size-8 items-center justify-center text-[11px] font-bold shadow-md hover:!z-30 focus-visible:!z-30 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-white ${markerClass(entry.layer)} ${selected ? "z-20 ring-2 ring-white ring-offset-2 ring-offset-slate-950" : "z-10"}`}
                  style={{ left: `${point.xPercent}%`, top: `${point.yPercent}%`, transform: `translate(-50%, -50%) scale(${1 / camera.zoom})` }}>
                  {entry.layer === "objectives" ? <span aria-hidden="true" className="absolute inset-0 rotate-45 border-2 border-white bg-sky-500" /> : null}
                  <span className="relative">{entry.layer === "you" ? "Y" : entry.layer === "enemy" ? "E" : entry.layer === "combat" ? "!" : "◆"}</span>
                  {selected ? <span aria-hidden="true" className={`absolute whitespace-nowrap rounded bg-slate-950 px-1.5 py-0.5 text-xs text-white ${point.xPercent > 65 ? "right-full mr-2" : "left-full ml-2"}`}>{time(entry.timestampMs, true)}</span> : null}
                </button>;
              })}
            </div>
          </div>
        )}
        <p id={`${detailId}-camera-help`} className="mt-2 text-[11px] text-muted-foreground">Scroll to zoom; drag or use arrow keys to pan while zoomed. Y/E circles are frame samples; ! and ◆ are event-reported positions. No path is inferred.</p>
      </div>
      <SpatialEvidenceDetails entry={active} detailId={detailId} windowStartMs={window.requestedStartTimestampMs} />
      <SampleDisclosure entries={samples} activeId={activeId} onSelect={onActiveChange} detailId={detailId} windowStartMs={window.requestedStartTimestampMs} />
      <details className="mt-3 border-t pt-3 text-[11px] text-muted-foreground">
        <summary className="cursor-pointer font-medium">Map notes and attribution</summary>
        <p className="mt-2">Static reference artwork; not reconstructed terrain or objective availability. Map artwork © Riot Games. Pathwise is not endorsed by Riot Games and does not reflect the views or opinions of Riot Games or anyone officially involved in producing or managing Riot Games properties. Riot Games and all associated properties are trademarks or registered trademarks of Riot Games, Inc. <a href="https://developer.riotgames.com/docs/lol#data-dragon_other" className="underline" target="_blank" rel="noreferrer">Asset source</a> · <a href="https://www.riotgames.com/en/legal" className="underline" target="_blank" rel="noreferrer">Riot legal</a> · <a href="https://developer.riotgames.com/terms" className="underline" target="_blank" rel="noreferrer">API terms</a>.</p>
      </details>
    </section>;

  return expanded
    ? createPortal(<><div aria-hidden="true" className="fixed inset-0 z-40 bg-black/70 backdrop-blur-sm" onMouseDown={() => setExpanded(false)} />{content}</>, document.body)
    : content;
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
