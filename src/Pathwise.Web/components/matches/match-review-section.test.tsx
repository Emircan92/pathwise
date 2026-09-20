import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { MatchReviewContent, MatchReviewSection, formatMatchTime } from "./match-review-section";
import { getMatchReview, type MatchReview } from "@/lib/api/pathwise";

vi.mock("@/lib/api/pathwise", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/pathwise")>();
  return { ...actual, getMatchReview: vi.fn() };
});

const mockedGetMatchReview = vi.mocked(getMatchReview);

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

describe("match review presentation", () => {
  it("renders every observation and annotation variant chronologically with precise evidence", () => {
    const review = representativeReview();
    render(<MatchReviewContent review={review} />);

    const cards = screen.getAllByRole("article");
    expect(within(cards[0]).getByRole("heading", { name: "5:00–6:00" })).toBeInTheDocument();
    expect(within(cards[1]).getByRole("heading", { name: "23:41–27:00" })).toBeInTheDocument();
    expect(screen.getByText("Relative gold:")).toBeInTheDocument();
    expect(screen.getByText("+4,742 → +3,182 (−1,560)")).toBeInTheDocument();
    expect(screen.getByText("−856 → −4,182 (−3,326)")).toBeInTheDocument();
    expect(screen.getByText("−4 → −25 (−21)")).toBeInTheDocument();
    expect(screen.getByText((_, element) => element?.tagName === "P" && element.textContent === "Combat: 0 kills / 2 deaths / 1 assist")).toBeInTheDocument();
    expect(screen.getByText((_, element) => element?.tagName === "P" && element.textContent === "Objectives: Hextech Dragon, Baron Nashor")).toBeInTheDocument();
    expect(screen.getByText("Selected for relative gold and XP change")).toBeInTheDocument();
    expect(screen.getByText("Relative XP:")).toBeInTheDocument();
    expect(screen.getByText("Associated recorded kill: Hextech Dragon at 23:47")).toHaveClass("text-foreground/80");
    expect(screen.getByText("Associated recorded kill: Baron Nashor at 25:05")).toHaveClass("text-foreground/80");
    expect(screen.getByText((_, element) => element?.tagName === "P" && element.textContent === "Initial-spawn context · Elemental Dragon initial spawn: 5:00")).toHaveClass("text-xs");
    expect(screen.getByText("Metric source frames · 23:00–27:00")).toHaveClass("text-[11px]");

    const evidence = within(cards[1]).getByText("Evidence and sources").closest("details");
    expect(evidence).not.toHaveAttribute("open");
    fireEvent.click(within(cards[1]).getByText("Evidence and sources"));
    expect(evidence).toHaveAttribute("open");
    expect(screen.getByText("Review bounds: 23:41.654–27:00.500")).toBeInTheDocument();
    expect(screen.getByText("Metric frames: 23:00.440 (frame 23)–27:00.500 (frame 27)")).toBeInTheDocument();
    expect(screen.getByText("Signal types: Gold difference change, XP difference change")).toBeInTheDocument();
    expect(within(cards[1]).getByText("Events after the start, through the end.")).toBeInTheDocument();
    expect(within(cards[1]).getByRole("link", { name: "Riot objectives" })).toHaveAttribute("href", "https://example.com/objectives");

    const renderedText = document.body.textContent ?? "";
    expect(renderedText).not.toMatch(/\bxp\b/);
    const visibleText = renderedText.toLowerCase();
    for (const phrase of ["lost your lead", "fell behind badly", "bad fight", "mistake", "good decision", "should have reset", "should have contested", "missed opportunity"]) expect(visibleText).not.toContain(phrase);
  });

  it.each([
    [0, 1, 0, "Combat: 0 kills / 1 death / 0 assists"],
    [1, 2, 1, "Combat: 1 kill / 2 deaths / 1 assist"],
    [1, 1, 1, "Combat: 1 kill / 1 death / 1 assist"],
  ])("pluralizes combat counts for %i/%i/%i", (kills, deaths, assists, expected) => {
    const review = representativeReview();
    review.windows = [{
      ...review.windows[0],
      observations: [{ kind: "configuredPlayerCombat", kills, deaths, assists, distinctEventCount: kills + deaths + assists, events: [] }],
      knowledgeAnnotations: [],
    }];

    render(<MatchReviewContent review={review} />);

    expect(screen.getByText((_, element) => element?.tagName === "P" && element.textContent === expected)).toBeInTheDocument();
  });

  it("formats match-relative timestamps without wrapping after an hour", () => {
    expect(formatMatchTime(1_380_440)).toBe("23:00");
    expect(formatMatchTime(1_421_654)).toBe("23:41");
    expect(formatMatchTime(1_380_440, true)).toBe("23:00.440");
    expect(formatMatchTime(1_421_654, true)).toBe("23:41.654");
    expect(formatMatchTime(3_661_999)).toBe("61:01");
    expect(formatMatchTime(3_661_999, true)).toBe("61:01.999");
  });

  it("renders zero, partial, unresolved-enemy, omission, and coverage states without invented zeros", () => {
    const review = representativeReview();
    review.enemyResolution = { status: "ambiguous", participantId: null };
    review.knowledge = { publicPatch: null, coverage: "unknownPatch" };
    review.windows = [{ ...review.windows[0], observations: [], metricOmissions: [{ kind: "relativeGoldMovement", reason: "counterRegression" }], knowledgeAnnotations: [] }];
    const { rerender } = render(<MatchReviewContent review={review} />);

    expect(screen.getByText("Relative metrics are unavailable because the opposing jungler could not be resolved.")).toBeInTheDocument();
    expect(screen.getByText("No factual observations were emitted for this period.")).toBeInTheDocument();
    expect(screen.getByText("Relative gold was omitted because the source counters were inconsistent.")).toBeInTheDocument();
    expect(screen.getByText("Game context unavailable: the public patch could not be resolved.")).toBeInTheDocument();
    expect(screen.queryByText(/Combat:/)).not.toBeInTheDocument();

    rerender(<MatchReviewContent review={{ ...review, knowledge: { publicPatch: "26.19", coverage: "noPackForPatch" }, windows: [] }} />);
    expect(screen.getByText("Game context unavailable: no knowledge pack exists for patch 26.19.")).toBeInTheDocument();
    expect(screen.getByText("No review periods were selected for this match.")).toBeInTheDocument();

    rerender(<MatchReviewContent review={{ ...review, knowledge: { publicPatch: "26.18", coverage: "unsupportedMatch" }, windows: [] }} />);
    expect(screen.getByText("Game context unavailable: the knowledge pack does not cover this match’s map or queue.")).toBeInTheDocument();
  });
});

describe("match review request lifecycle", () => {
  it("shows API detail and retries manually", async () => {
    mockedGetMatchReview.mockRejectedValueOnce(new Error("Stored timeline is unavailable.")).mockResolvedValueOnce(representativeReview());
    render(<MatchReviewSection matchId="EUW1_1" />);
    expect(screen.getByText("Loading match review…")).toBeInTheDocument();
    expect(await screen.findByText("Stored timeline is unavailable.")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Retry" }));
    expect(screen.getByText("Loading match review…")).toBeInTheDocument();
    expect(await screen.findByRole("heading", { name: "23:41–27:00" })).toBeInTheDocument();
    expect(mockedGetMatchReview).toHaveBeenCalledTimes(2);
  });

  it("aborts obsolete requests on match changes", async () => {
    let firstSignal: AbortSignal | undefined;
    mockedGetMatchReview.mockImplementationOnce((_matchId, signal) => {
      firstSignal = signal;
      return new Promise(() => undefined);
    }).mockResolvedValueOnce({ ...representativeReview(), matchId: "EUW1_2", windows: [] });
    const { rerender } = render(<MatchReviewSection matchId="EUW1_1" />);
    rerender(<MatchReviewSection matchId="EUW1_2" />);
    await waitFor(() => expect(firstSignal?.aborted).toBe(true));
    expect(await screen.findByText("No review periods were selected for this match.")).toBeInTheDocument();
  });
});

function representativeReview(): MatchReview {
  const dragon = { source: { frameIndex: 24, eventIndex: 30 }, timestampMs: 1_427_715, monsterType: "DRAGON", monsterSubType: "HEXTECH_DRAGON", killerParticipantId: 7, assistingParticipantIds: [8], teamAttribution: { kind: "KnownTeam" as const, suppliedTeamId: 200, resolvedTeamId: 200, diagnosticReason: null } };
  const baron = { source: { frameIndex: 26, eventIndex: 7 }, timestampMs: 1_505_113, monsterType: "BARON_NASHOR", monsterSubType: null, killerParticipantId: 7, assistingParticipantIds: [8], teamAttribution: { kind: "KnownTeam" as const, suppliedTeamId: 200, resolvedTeamId: 200, diagnosticReason: null } };
  const elementalFact = { id: "elemental-dragon.initial-spawn", objective: "elementalDragon" as const, initialSpawnTimestampMs: 300_000, sources: [{ title: "Riot objectives", url: "https://example.com/objectives" }] };
  const baronFact = { id: "baron-nashor.initial-spawn", objective: "baronNashor" as const, initialSpawnTimestampMs: 1_200_000, sources: [{ title: "Riot Baron", url: "https://example.com/baron" }] };
  return {
    matchId: "EUW1_1",
    configuredParticipantId: 2,
    enemyResolution: { status: "resolved", participantId: 7 },
    versions: { reconstruction: 1, detector: 2, factualObservations: 1, knowledgeAnnotations: 1 },
    knowledge: { publicPatch: "26.18", coverage: "available" },
    sourceDataIssues: [],
    windows: [
      {
        requestedStartTimestampMs: 1_421_654, requestedEndTimestampMs: 1_620_500,
        startFrame: { frameIndex: 23, timestampMs: 1_380_440 }, endFrame: { frameIndex: 27, timestampMs: 1_620_500 },
        selectionRank: 1, primarySelectionReason: "goldAndXpChange", signalKinds: ["goldDifferenceChange", "xpDifferenceChange"], absorbedSignalKinds: ["configuredPlayerDeath"],
        observations: [
          { kind: "relativeGoldMovement", relativeStart: 4742, relativeEnd: 3182, signedChange: -1560, configuredPlayer: { startValue: 14650, endValue: 16243 }, enemyJungler: { startValue: 9908, endValue: 13061 } },
          { kind: "relativeXpMovement", relativeStart: -856, relativeEnd: -4182, signedChange: -3326, configuredPlayer: { startValue: 12401, endValue: 13501 }, enemyJungler: { startValue: 13257, endValue: 17683 } },
          { kind: "relativeJungleCsMovement", relativeStart: -4, relativeEnd: -25, signedChange: -21, configuredPlayer: { startValue: 154, endValue: 165 }, enemyJungler: { startValue: 158, endValue: 190 } },
          { kind: "configuredPlayerCombat", kills: 0, deaths: 2, assists: 1, distinctEventCount: 3, events: [{ source: { frameIndex: 24, eventIndex: 12 }, timestampMs: 1_425_000, killerParticipantId: 7, victimParticipantId: 2, assistingParticipantIds: [] }] },
          { kind: "eliteObjectiveContext", events: [dragon, baron] },
        ],
        metricOmissions: [],
        knowledgeAnnotations: [
          { kind: "recordedObjectiveContext", fact: elementalFact, target: { observationKind: "eliteObjectiveContext", event: dragon.source }, recordedKillTimestampMs: dragon.timestampMs },
          { kind: "recordedObjectiveContext", fact: baronFact, target: { observationKind: "eliteObjectiveContext", event: baron.source }, recordedKillTimestampMs: baron.timestampMs },
        ],
      },
      {
        requestedStartTimestampMs: 300_000, requestedEndTimestampMs: 360_000,
        startFrame: { frameIndex: 5, timestampMs: 300_000 }, endFrame: { frameIndex: 6, timestampMs: 360_000 },
        selectionRank: 2, primarySelectionReason: "configuredPlayerDeath", signalKinds: ["configuredPlayerDeath"], absorbedSignalKinds: [], observations: [], metricOmissions: [],
        knowledgeAnnotations: [{ kind: "nearInitialSpawn", fact: elementalFact, target: { observationKind: null, event: null }, recordedKillTimestampMs: null }],
      },
    ],
  };
}
