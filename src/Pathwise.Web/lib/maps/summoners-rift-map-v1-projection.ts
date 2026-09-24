import type { Position } from "@/lib/api/pathwise";

// Empirical fit and its independent checks are recorded in docs/map-v1-calibration.md.
export const summonersRiftMapV1Projection = {
  assetVersion: "16.18.1",
  assetSha256: "5b446777c3e8491c1ab1860bc8fd448ad58f46cf3630d909f74dd3dc3dda8cd1",
  calibrationVersion: "map11-16.18.1-fixture-001",
  width: 512,
  height: 512,
  xScale: 0.03606760098928277,
  xOffset: -10.590478153338836,
  yScale: -0.03445990722332671,
  yOffset: 512.8296885354539,
} as const;

export function projectSummonersRiftPosition(position: Position): { xPercent: number; yPercent: number } | null {
  if (!Number.isFinite(position.x) || !Number.isFinite(position.y)) return null;
  const imageX = summonersRiftMapV1Projection.xScale * position.x + summonersRiftMapV1Projection.xOffset;
  const imageY = summonersRiftMapV1Projection.yScale * position.y + summonersRiftMapV1Projection.yOffset;
  if (imageX < 0 || imageX > summonersRiftMapV1Projection.width || imageY < 0 || imageY > summonersRiftMapV1Projection.height) return null;
  return { xPercent: imageX / summonersRiftMapV1Projection.width * 100, yPercent: imageY / summonersRiftMapV1Projection.height * 100 };
}
