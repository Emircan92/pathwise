import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { MatchDetail } from "./match-detail";
import { getMatch, getMatchReview } from "@/lib/api/pathwise";

vi.mock("next/link", () => ({ default: ({ children, href }: { children: React.ReactNode; href: string }) => <a href={href}>{children}</a> }));
vi.mock("@/lib/api/pathwise", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/pathwise")>();
  return { ...actual, getMatch: vi.fn(), getMatchReview: vi.fn() };
});

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

it("keeps factual match detail visible when review loading fails", async () => {
  vi.mocked(getMatch).mockResolvedValue({
    matchId: "EUW1_1", queueId: 420, playedAtUtc: "2026-09-20T12:00:00Z", durationSeconds: 1691,
    championName: "Kha'Zix", won: false, teamPosition: "JUNGLE", supportStatus: "jungle", ingestionStatus: "complete",
    matchPayloadAvailable: true, timelinePayloadAvailable: true, failures: [],
  });
  vi.mocked(getMatchReview).mockRejectedValue(new Error("Review endpoint failed."));

  render(<MatchDetail matchId="EUW1_1" />);
  expect(await screen.findByRole("heading", { name: "Kha'Zix" })).toBeInTheDocument();
  expect(screen.getByText("28:11")).toBeInTheDocument();
  expect(screen.getByText("Match payload")).toBeInTheDocument();
  expect(await screen.findByText("Match review unavailable")).toBeInTheDocument();
  expect(screen.getByText("Review endpoint failed.")).toBeInTheDocument();
});
