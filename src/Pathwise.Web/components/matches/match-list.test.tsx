import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { RecentMatches } from "./match-list";
import { fetchLatestMatches, getMatches, getPlayer, type Match, type MatchList } from "@/lib/api/pathwise";

vi.mock("next/link", () => ({ default: ({ children, href }: { children: React.ReactNode; href: string }) => <a href={href}>{children}</a> }));
vi.mock("@/lib/api/pathwise", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/pathwise")>();
  return { ...actual, getPlayer: vi.fn(), getMatches: vi.fn(), fetchLatestMatches: vi.fn() };
});

const match = (id: number, incomplete = false): Match => ({
  matchId: `EUW1_${id}`, queueId: 420, playedAtUtc: new Date(2026, 8, 25, 0, id).toISOString(), durationSeconds: 1800,
  championName: incomplete ? null : `Champion ${id}`, won: true, teamPosition: "JUNGLE", supportStatus: "jungle",
  ingestionStatus: incomplete ? "incomplete" : "complete", matchPayloadAvailable: !incomplete, timelinePayloadAvailable: !incomplete, failures: [],
});
const page = (totalStored: number, offset: number, rows: Match[]): MatchList => ({
  totalStored, limit: 50, offset, matches: rows.filter((row) => row.championName !== null), incompleteImports: rows.filter((row) => row.championName === null),
});

afterEach(() => { cleanup(); vi.resetAllMocks(); });

function readyPlayer() {
  vi.mocked(getPlayer).mockResolvedValue({ gameName: "Player", tagLine: "EUW", platform: "euw1", regional: "europe", fetchCount: 50, fetchReady: true, configurationErrors: [], puuid: "p", resolvedAtUtc: null });
}

it("shows the stored total and appends the next combined page without duplicates", async () => {
  readyPlayer();
  const first = page(52, 0, Array.from({ length: 50 }, (_, index) => match(52 - index, index === 1)));
  const second = page(52, 50, [match(2), match(1)]);
  vi.mocked(getMatches).mockResolvedValueOnce(first).mockResolvedValueOnce(second);

  render(<RecentMatches />);
  expect(await screen.findByText("52 stored")).toBeInTheDocument();
  expect(getMatches).toHaveBeenCalledWith();
  expect(screen.getAllByRole("link")).toHaveLength(50);
  fireEvent.click(screen.getByRole("button", { name: "Load more" }));
  await waitFor(() => expect(getMatches).toHaveBeenCalledWith(50, 50));
  expect(await screen.findByText("Champion 2")).toBeInTheDocument();
  expect(screen.getAllByRole("link")).toHaveLength(52);
  expect(screen.queryByRole("button", { name: "Load more" })).not.toBeInTheDocument();
});

it("keeps loaded rows after a failed page and deduplicates a repeated row", async () => {
  readyPlayer();
  vi.mocked(getMatches).mockResolvedValueOnce(page(3, 0, [match(3), match(2)])).mockRejectedValueOnce(new Error("Page failed"))
    .mockResolvedValueOnce(page(3, 2, [match(2)]));
  render(<RecentMatches />);
  expect(await screen.findByText("3 stored")).toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "Load more" }));
  expect(await screen.findByRole("alert")).toHaveTextContent("Page failed");
  expect(screen.getAllByRole("link")).toHaveLength(2);
  fireEvent.click(screen.getByRole("button", { name: "Load more" }));
  await waitFor(() => expect(screen.queryByRole("button", { name: "Load more" })).not.toBeInTheDocument());
  expect(screen.getAllByRole("link")).toHaveLength(2);
});

it("resets to the newest page and updated total after fetching", async () => {
  readyPlayer();
  vi.mocked(getMatches).mockResolvedValueOnce(page(3, 0, [match(3), match(2)]))
    .mockResolvedValueOnce(page(3, 2, [match(1)]))
    .mockResolvedValueOnce(page(4, 0, [match(4), match(3)]));
  vi.mocked(fetchLatestMatches).mockResolvedValue({ outcome: "succeeded", requestedCount: 50, discoveredCount: 1, newlyCompleted: ["EUW1_4"], repaired: [], alreadyComplete: [], incomplete: [], deferred: [], errors: [], retryAfterUtc: null });
  render(<RecentMatches />);
  expect(await screen.findByText("3 stored")).toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "Load more" }));
  expect(await screen.findByText("Champion 1")).toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "Fetch Latest Matches" }));
  expect(await screen.findByText("4 stored")).toBeInTheDocument();
  expect(screen.getByText("Champion 4")).toBeInTheDocument();
  expect(screen.queryByText("Champion 1")).not.toBeInTheDocument();
  expect(screen.getByRole("button", { name: "Load more" })).toBeInTheDocument();
});
