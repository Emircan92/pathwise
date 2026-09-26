import { describe, expect, it } from "vitest";
import {
  FIT_MAP_CAMERA,
  clampMapCamera,
  focusMapCamera,
  panMapCamera,
  zoomMapCameraAt,
} from "./map-camera";

const viewport = { width: 600, height: 600 };

describe("map camera", () => {
  it("zooms around the pointer while retaining the anchored map point", () => {
    const zoomed = zoomMapCameraAt(FIT_MAP_CAMERA, 2, { x: 450, y: 300 }, viewport);

    expect(zoomed).toEqual({ zoom: 2, panX: -150, panY: 0 });
  });

  it("bounds pan so the useful map always covers the viewport", () => {
    const panned = panMapCamera({ zoom: 2, panX: 0, panY: 0 }, { x: 1_000, y: -1_000 }, viewport);

    expect(panned).toEqual({ zoom: 2, panX: 300, panY: -300 });
    expect(clampMapCamera({ zoom: 1, panX: 40, panY: -20 }, viewport)).toEqual(FIT_MAP_CAMERA);
  });

  it("focuses a selected point without exposing space beyond the map", () => {
    expect(focusMapCamera({ xPercent: 75, yPercent: 25 }, viewport)).toEqual({ zoom: 2.5, panX: -375, panY: 375 });
    expect(focusMapCamera({ xPercent: 100, yPercent: 100 }, viewport)).toEqual({ zoom: 2.5, panX: -450, panY: -450 });
  });

  it("clamps zoom to the supported camera range", () => {
    expect(zoomMapCameraAt(FIT_MAP_CAMERA, 20, { x: 300, y: 300 }, viewport).zoom).toBe(4);
    expect(zoomMapCameraAt({ zoom: 2, panX: 0, panY: 0 }, 0, { x: 300, y: 300 }, viewport)).toEqual(FIT_MAP_CAMERA);
  });
});
