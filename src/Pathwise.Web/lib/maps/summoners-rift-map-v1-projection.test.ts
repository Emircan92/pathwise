import { describe, expect, it } from "vitest";
import { projectSummonersRiftPosition as project, summonersRiftMapV1Projection as fit } from "./summoners-rift-map-v1-projection";

describe("Map V1 projection", () => {
  it("uses the measured orientation and fitted pit landmarks", () => {
    expect(fit.xScale).toBeGreaterThan(0);
    expect(fit.yScale).toBeLessThan(0);
    const baron = project({ x: 5007, y: 10471 })!;
    const dragon = project({ x: 9859, y: 4435 })!;
    expect(baron.xPercent * fit.width / 100).toBeCloseTo(170, 5);
    expect(baron.yPercent * fit.height / 100).toBeCloseTo(152, 5);
    expect(dragon.xPercent * fit.width / 100).toBeCloseTo(345, 5);
    expect(dragon.yPercent * fit.height / 100).toBeCloseTo(360, 5);
  });

  it("places held-out objectives in their measured pit regions", () => {
    for (const point of [{ x: 10020, y: 5038 }, { x: 9926, y: 4522 }, { x: 9837, y: 4397 }]) {
      const projected = project(point)!;
      expect(projected.xPercent * 5.12).toBeGreaterThanOrEqual(330);
      expect(projected.xPercent * 5.12).toBeLessThanOrEqual(365);
      expect(projected.yPercent * 5.12).toBeGreaterThanOrEqual(335);
      expect(projected.yPercent * 5.12).toBeLessThanOrEqual(385);
    }
    for (const point of [{ x: 4790, y: 10182 }, { x: 5210, y: 10424 }, { x: 4841, y: 10638 }, { x: 4586, y: 10379 }]) {
      const projected = project(point)!;
      expect(projected.xPercent * 5.12).toBeGreaterThanOrEqual(145);
      expect(projected.xPercent * 5.12).toBeLessThanOrEqual(190);
      expect(projected.yPercent * 5.12).toBeGreaterThanOrEqual(130);
      expect(projected.yPercent * 5.12).toBeLessThanOrEqual(175);
    }
  });

  it("suppresses invalid and out-of-image markers without clamping source evidence", () => {
    expect(project({ x: Number.NaN, y: 5000 })).toBeNull();
    expect(project({ x: 5000, y: Number.POSITIVE_INFINITY })).toBeNull();
    expect(project({ x: -1000, y: 5000 })).toBeNull();
    expect(project({ x: 5000, y: -1000 })).toBeNull();
  });

  it("returns normalized placement for any rendered map size", () => {
    const point = project({ x: 9837, y: 4397 })!;
    expect(point.xPercent / 100 * 256).toBeCloseTo(point.xPercent / 100 * 512 / 2, 6);
  });
});
