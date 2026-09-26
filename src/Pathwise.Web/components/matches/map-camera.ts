export const FIT_MAP_CAMERA = { zoom: 1, panX: 0, panY: 0 } as const;
export const MIN_MAP_ZOOM = 1;
export const MAX_MAP_ZOOM = 4;
export const MAP_ZOOM_STEP = 0.5;

export type MapCamera = { zoom: number; panX: number; panY: number };
export type MapViewportSize = { width: number; height: number };
export type MapPoint = { x: number; y: number };

function clamp(value: number, minimum: number, maximum: number): number {
  return Math.min(maximum, Math.max(minimum, value));
}

export function clampMapZoom(zoom: number): number {
  return clamp(zoom, MIN_MAP_ZOOM, MAX_MAP_ZOOM);
}

export function clampMapCamera(camera: MapCamera, viewport: MapViewportSize): MapCamera {
  const zoom = clampMapZoom(camera.zoom);
  const maximumPanX = Math.max(0, (zoom - 1) * viewport.width / 2);
  const maximumPanY = Math.max(0, (zoom - 1) * viewport.height / 2);
  return {
    zoom,
    panX: maximumPanX === 0 ? 0 : clamp(camera.panX, -maximumPanX, maximumPanX),
    panY: maximumPanY === 0 ? 0 : clamp(camera.panY, -maximumPanY, maximumPanY),
  };
}

export function zoomMapCameraAt(camera: MapCamera, nextZoom: number, anchor: MapPoint, viewport: MapViewportSize): MapCamera {
  const zoom = clampMapZoom(nextZoom);
  const relativeX = anchor.x - viewport.width / 2;
  const relativeY = anchor.y - viewport.height / 2;
  const contentX = (relativeX - camera.panX) / camera.zoom;
  const contentY = (relativeY - camera.panY) / camera.zoom;
  return clampMapCamera({
    zoom,
    panX: relativeX - zoom * contentX,
    panY: relativeY - zoom * contentY,
  }, viewport);
}

export function panMapCamera(camera: MapCamera, delta: MapPoint, viewport: MapViewportSize): MapCamera {
  return clampMapCamera({ ...camera, panX: camera.panX + delta.x, panY: camera.panY + delta.y }, viewport);
}

export function focusMapCamera(
  pointPercent: { xPercent: number; yPercent: number },
  viewport: MapViewportSize,
  zoom = 2.5,
): MapCamera {
  const clampedZoom = clampMapZoom(zoom);
  const pointX = pointPercent.xPercent / 100 * viewport.width;
  const pointY = pointPercent.yPercent / 100 * viewport.height;
  return clampMapCamera({
    zoom: clampedZoom,
    panX: -clampedZoom * (pointX - viewport.width / 2),
    panY: -clampedZoom * (pointY - viewport.height / 2),
  }, viewport);
}
