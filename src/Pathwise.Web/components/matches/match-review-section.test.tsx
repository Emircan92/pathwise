import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { MatchReviewContent, MatchReviewSection, formatMatchTime } from "./match-review-section";
import { getMatchReview, type Encounter, type MatchReview } from "@/lib/api/pathwise";
import { buildSpatialEvidence } from "./spatial-evidence";

vi.mock("@/lib/api/pathwise", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/pathwise")>();
  return { ...actual, getMatchReview: vi.fn() };
});

const mockedGetMatchReview = vi.mocked(getMatchReview);

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

describe("hierarchical match review", () => {
  it("opens the detector rank-1 period while keeping periods in chronological navigation order", () => {
    render(<MatchReviewContent review={representativeReview()} matchDurationMs={1_800_000} />);

    expect(screen.getByText("Period 2 of 2")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "23:41–27:00" })).toBeInTheDocument();
    expect(screen.getByText(/Most important/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Review period 2 of 2.*most important/ })).toHaveAttribute("aria-current", "step");
    expect(screen.getByRole("button", { name: "Previous review period" })).toBeEnabled();
    expect(screen.getByRole("button", { name: "Next review period" })).toBeDisabled();
  });

  it("moves through review periods chronologically and resets to each period's initial scope", () => {
    render(<MatchReviewContent review={representativeReview()} />);

    expect(screen.getByRole("button", { name: "Encounter 1 · 23:45.000" })).toHaveAttribute("aria-pressed", "true");
    fireEvent.click(screen.getByRole("button", { name: "Previous review period" }));
    expect(screen.getByText("Period 1 of 2")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "5:00–6:00" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Previous review period" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Next review period" })).toBeEnabled();
    expect(screen.getByRole("button", { name: "Window overview" })).toHaveAttribute("aria-pressed", "true");

    fireEvent.click(screen.getByRole("button", { name: "Next review period" }));
    expect(screen.getByText("Period 2 of 2")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Encounter 1 · 23:45.000" })).toHaveAttribute("aria-pressed", "true");
  });

  it("initially selects the first chronological encounter with unmistakable visual state", () => {
    const review = reviewWithSecondEncounter();
    render(<MatchReviewContent review={review} />);

    const first = screen.getByRole("button", { name: "Encounter 1 · 23:45.000" });
    const second = screen.getByRole("button", { name: "Encounter 2 · 25:00.000–25:05.000" });
    expect(first).toHaveAttribute("aria-pressed", "true");
    expect(first.closest("li")).toHaveAttribute("data-state", "selected");
    expect(first.closest("li")).toHaveClass("border-primary", "ring-primary/30");
    expect(second).toHaveAttribute("aria-pressed", "false");
    expect(screen.getByLabelText("Encounter events")).toHaveTextContent("You died");
  });

  it("switches encounters and displays only the selected encounter's events", () => {
    render(<MatchReviewContent review={reviewWithSecondEncounter()} />);

    fireEvent.click(screen.getByRole("button", { name: "Encounter 2 · 25:00.000–25:05.000" }));
    expect(screen.getByRole("button", { name: "Encounter 1 · 23:45.000" })).toHaveAttribute("aria-pressed", "false");
    expect(screen.getByRole("button", { name: "Encounter 2 · 25:00.000–25:05.000" })).toHaveAttribute("aria-pressed", "true");
    const events = screen.getByLabelText("Encounter events");
    expect(within(events).getByRole("button", { name: /Recorded champion kill, 25:00\.000/ })).toBeInTheDocument();
    expect(within(events).getByRole("button", { name: /You killed a champion, 25:05\.000/ })).toBeInTheDocument();
    expect(within(events).queryByRole("button", { name: /You died/ })).not.toBeInTheDocument();
  });

  it("treats Window overview as a separate scope and lets a map event reveal its encounter", () => {
    render(<MatchReviewContent review={reviewWithSecondEncounter()} />);

    fireEvent.click(screen.getByRole("button", { name: "Window overview" }));
    expect(screen.getByRole("button", { name: "Window overview" })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("button", { name: "Encounter 1 · 23:45.000" })).toHaveAttribute("aria-pressed", "false");
    expect(screen.getByLabelText("Current review scope")).toHaveTextContent("Window overview");

    const inspector = screen.getByLabelText("Synchronized evidence inspector");
    fireEvent.click(within(inspector).getByRole("button", { name: "You killed a champion, 25:05.000, recorded event" }));
    expect(screen.getByRole("button", { name: "Encounter 2 · 25:00.000–25:05.000" })).toHaveAttribute("aria-pressed", "true");
    expect(within(screen.getByLabelText("Encounter events")).getByRole("button", { name: /You killed a champion, 25:05\.000/ })).toHaveAttribute("aria-pressed", "true");
  });

  it("keeps textual events and map markers synchronized in both directions", () => {
    render(<MatchReviewContent review={reviewWithSecondEncounter()} />);
    fireEvent.click(screen.getByRole("button", { name: "Encounter 2 · 25:00.000–25:05.000" }));

    const events = screen.getByLabelText("Encounter events");
    const inspector = screen.getByLabelText("Synchronized evidence inspector");
    const secondEvent = within(events).getByRole("button", { name: /You killed a champion, 25:05\.000/ });
    fireEvent.click(secondEvent);
    expect(within(inspector).getByRole("button", { name: "You killed a champion, 25:05.000, recorded event" })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("heading", { name: "You killed a champion · 25:05.000" })).toBeInTheDocument();

    fireEvent.click(within(inspector).getByRole("button", { name: "Recorded champion kill, 25:00.000, recorded event" }));
    expect(within(events).getByRole("button", { name: /Recorded champion kill, 25:00\.000/ })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("heading", { name: "Recorded champion kill · 25:00.000" })).toBeInTheDocument();
  });

  it("resets active evidence when changing encounter and period", () => {
    render(<MatchReviewContent review={reviewWithSecondEncounter()} />);
    fireEvent.click(screen.getByRole("button", { name: "Encounter 2 · 25:00.000–25:05.000" }));
    fireEvent.click(within(screen.getByLabelText("Encounter events")).getByRole("button", { name: /You killed a champion, 25:05\.000/ }));
    expect(screen.getByRole("heading", { name: "You killed a champion · 25:05.000" })).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Encounter 1 · 23:45.000" }));
    expect(screen.getByRole("heading", { name: "You died · 23:45.000" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Previous review period" }));
    expect(screen.getByRole("button", { name: "Window overview" })).toHaveAttribute("aria-pressed", "true");
    fireEvent.click(screen.getByRole("button", { name: "Next review period" }));
    expect(screen.getByRole("button", { name: "Encounter 1 · 23:45.000" })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("heading", { name: "You died · 23:45.000" })).toBeInTheDocument();
  });

  it("keeps events without map positions selectable and explicit", () => {
    render(<MatchReviewContent review={representativeReview()} />);

    const event = within(screen.getByLabelText("Encounter events")).getByRole("button", { name: "You died, 23:45.000, No recorded map location" });
    fireEvent.click(event);
    expect(event).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("heading", { name: "You died · 23:45.000" })).toBeInTheDocument();
    expect(screen.getAllByText("No recorded map location").length).toBeGreaterThan(0);
    expect(screen.queryByRole("button", { name: "You died, 23:45.000, recorded event" })).not.toBeInTheDocument();
  });

  it("renders observations and keeps context and provenance progressively disclosed", () => {
    render(<MatchReviewContent review={representativeReview()} />);

    expect(screen.getByText("Relative gold:")).toBeInTheDocument();
    expect(screen.getByText("+4,742 → +3,182 (−1,560)")).toBeInTheDocument();
    expect(screen.getByText("−856 → −4,182 (−3,326)")).toBeInTheDocument();
    expect(screen.getByText((_, element) => element?.tagName === "P" && element.textContent === "Combat: 0 kills / 2 deaths / 1 assist")).toBeInTheDocument();
    const gameContext = screen.getByText("Game context · Patch 26.18").closest("details");
    const sources = screen.getByText("Evidence and sources").closest("details");
    expect(gameContext).not.toHaveAttribute("open");
    expect(sources).not.toHaveAttribute("open");
    fireEvent.click(screen.getByText("Evidence and sources"));
    expect(screen.getByText("Review bounds: 23:41.654–27:00.500")).toBeInTheDocument();
    expect(screen.getByText("Signal types: Gold difference change, XP difference change")).toBeInTheDocument();
  });

  it("handles partial coverage and unresolved enemy state without invented values", () => {
    const review = representativeReview();
    review.enemyResolution = { status: "ambiguous", participantId: null };
    review.knowledge = { publicPatch: null, coverage: "unknownPatch" };
    review.windows = [{ ...review.windows[0], observations: [], metricOmissions: [{ kind: "relativeGoldMovement", reason: "counterRegression" }], knowledgeAnnotations: [] }];
    render(<MatchReviewContent review={review} />);

    expect(screen.getByText("Relative metrics are unavailable because the opposing jungler could not be resolved.")).toBeInTheDocument();
    expect(screen.getByLabelText("Enemy jungler")).toBeDisabled();
    expect(screen.getByText("No factual observations were emitted for this period.")).toBeInTheDocument();
    expect(screen.getByText("Relative gold was omitted because the source counters were inconsistent.")).toBeInTheDocument();
    expect(screen.getByText("Game context unavailable: the public patch could not be resolved.")).toBeInTheDocument();
  });

  it("preserves evidence when map artwork is unavailable", () => {
    const review = reviewWithSecondEncounter();
    render(<MatchReviewContent review={review} />);
    fireEvent.click(screen.getByRole("button", { name: "Encounter 2 · 25:00.000–25:05.000" }));
    fireEvent.error(screen.getByAltText("Static Summoner's Rift reference artwork"));
    expect(screen.getByText("Map artwork could not load. Recorded evidence remains available below.")).toBeInTheDocument();
    expect(screen.getByLabelText("Encounter events")).toHaveTextContent("Recorded champion kill");

    cleanup();
    render(<MatchReviewContent review={{ ...review, mapId: 12 }} />);
    expect(screen.getByText("Summoner's Rift artwork is unavailable for map 12. Recorded evidence remains available below.")).toBeInTheDocument();
  });

  it.each([
    [0, 1, 0, "Combat: 0 kills / 1 death / 0 assists"],
    [1, 2, 1, "Combat: 1 kill / 2 deaths / 1 assist"],
    [1, 1, 1, "Combat: 1 kill / 1 death / 1 assist"],
  ])("pluralizes combat counts for %i/%i/%i", (kills, deaths, assists, expected) => {
    const review = representativeReview();
    review.windows = [{ ...review.windows[0], observations: [{ kind: "configuredPlayerCombat", kills, deaths, assists, distinctEventCount: kills + deaths + assists, events: [] }], knowledgeAnnotations: [] }];
    render(<MatchReviewContent review={review} />);
    expect(screen.getByText((_, element) => element?.tagName === "P" && element.textContent === expected)).toBeInTheDocument();
  });

  it("formats match-relative timestamps without wrapping after an hour", () => {
    expect(formatMatchTime(1_380_440)).toBe("23:00");
    expect(formatMatchTime(1_421_654, true)).toBe("23:41.654");
    expect(formatMatchTime(3_661_999)).toBe("61:01");
  });
});

describe("spatial evidence semantics", () => {
  it("keeps post-death nearby samples out while preserving the recorded death location", () => {
    const review = representativeReview();
    const period = review.windows[0];
    period.encounters[0].combatEvents[0].position = { x: 9_000, y: 4_000 };
    period.positionSamples = [
      { frameIndex: 23, timestampMs: 1_424_000, configuredPlayerPosition: { x: 8_000, y: 4_000 }, enemyJunglerPosition: null },
      { frameIndex: 24, timestampMs: 1_425_000, configuredPlayerPosition: { x: 9_000, y: 4_000 }, enemyJunglerPosition: null },
      { frameIndex: 25, timestampMs: 1_430_000, configuredPlayerPosition: { x: 9_000, y: 4_000 }, enemyJunglerPosition: null },
      { frameIndex: 26, timestampMs: 1_440_000, configuredPlayerPosition: { x: 463, y: 692 }, enemyJunglerPosition: null },
    ];

    const evidence = buildSpatialEvidence(review, period, period.encounters[0]);
    expect(evidence.filter((entry) => entry.layer === "you").map((entry) => entry.timestampMs)).toEqual([1_424_000]);
    expect(evidence.find((entry) => entry.layer === "combat")).toMatchObject({ timestampMs: 1_425_000, position: { x: 9_000, y: 4_000 }, kind: "event", label: "You died" });
    expect(buildSpatialEvidence(review, period).filter((entry) => entry.layer === "you")).toHaveLength(4);
  });

  it("applies the death cutoff independently to each jungler", () => {
    const review = representativeReview();
    const period = review.windows[0];
    const encounter = period.encounters[0];
    encounter.combatEvents = [
      { ...encounter.combatEvents[0], victimParticipantId: 8, timestampMs: 1_424_000 },
      { ...encounter.combatEvents[0], source: { frameIndex: 24, eventIndex: 13 }, victimParticipantId: 7, timestampMs: 1_430_000, position: { x: 7_000, y: 5_000 } },
      { ...encounter.combatEvents[0], source: { frameIndex: 24, eventIndex: 14 }, victimParticipantId: 2, timestampMs: 1_445_000 },
    ];
    period.positionSamples = [1_425_000, 1_430_000, 1_435_000, 1_450_000].map((timestampMs, frameIndex) => ({ frameIndex, timestampMs, configuredPlayerPosition: { x: 8_000, y: 4_000 }, enemyJunglerPosition: { x: 7_000, y: 5_000 } }));

    const evidence = buildSpatialEvidence(review, period, encounter);
    expect(evidence.filter((entry) => entry.layer === "enemy").map((entry) => entry.timestampMs)).toEqual([1_425_000]);
    expect(evidence.filter((entry) => entry.layer === "you").map((entry) => entry.timestampMs)).toEqual([1_425_000, 1_430_000, 1_435_000]);
  });

  it("uses closed nearby sample bounds without substituting outside frames", () => {
    const review = representativeReview();
    const period = review.windows[0];
    period.encounters[0].combatEvents[0].victimParticipantId = 8;
    period.positionSamples = [1_421_653, 1_421_654, 1_455_000, 1_455_001].map((timestampMs, frameIndex) => ({ frameIndex, timestampMs, configuredPlayerPosition: null, enemyJunglerPosition: null }));
    const samples = buildSpatialEvidence(review, period, period.encounters[0]).filter((entry) => entry.kind === "sample");
    expect(samples.map((entry) => entry.timestampMs)).toEqual([1_421_654, 1_421_654, 1_455_000, 1_455_000]);
  });

  it("keeps equal-time evidence distinct", () => {
    const review = representativeReview();
    const period = review.windows[0];
    period.positionSamples[1] = { ...period.positionSamples[1], timestampMs: 1_427_715, configuredPlayerPosition: { x: 9837, y: 4397 }, enemyJunglerPosition: { x: 9837, y: 4397 } };
    period.encounters[0].combatEvents[0] = { ...period.encounters[0].combatEvents[0], timestampMs: 1_427_715, position: { x: 9837, y: 4397 } };
    const objectives = period.observations.find((observation) => observation.kind === "eliteObjectiveContext");
    if (objectives?.kind !== "eliteObjectiveContext") throw new Error("Test fixture lacks objectives");
    objectives.events.push({ ...objectives.events[0], source: { frameIndex: 24, eventIndex: 31 } });
    const simultaneous = buildSpatialEvidence(review, period).filter((entry) => entry.timestampMs === 1_427_715);
    expect(simultaneous.map((entry) => entry.layer)).toEqual(["you", "enemy", "combat", "objectives", "objectives"]);
    expect(new Set(simultaneous.map((entry) => entry.id)).size).toBe(5);
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
    mockedGetMatchReview.mockImplementationOnce((_matchId, signal) => { firstSignal = signal; return new Promise(() => undefined); })
      .mockResolvedValueOnce({ ...representativeReview(), matchId: "EUW1_2", windows: [] });
    const { rerender } = render(<MatchReviewSection matchId="EUW1_1" />);
    rerender(<MatchReviewSection matchId="EUW1_2" />);
    await waitFor(() => expect(firstSignal?.aborted).toBe(true));
    expect(await screen.findByText("No review periods were selected for this match.")).toBeInTheDocument();
  });
});

function reviewWithSecondEncounter(): MatchReview {
  const review = representativeReview();
  const period = review.windows[0];
  period.encounters.push(secondEncounter());
  return review;
}

function secondEncounter(): Encounter {
  return {
    id: "enc-v1-second", startTimestampMs: 1_500_000, endTimestampMs: 1_505_000, recordedEventSpanMs: 5_000,
    combatEventCount: 2, participantIds: [2, 7, 8], distinctParticipantCount: 3,
    configuredPlayerSummary: { involved: true, kills: 1, deaths: 0, assists: 0, distinctEventCount: 1 },
    enemyJunglerInvolved: true,
    combatEvents: [
      { source: { frameIndex: 25, eventIndex: 13 }, timestampMs: 1_500_000, killerParticipantId: 7, victimParticipantId: 8, assistingParticipantIds: [], position: { x: 4_000, y: 4_000 } },
      { source: { frameIndex: 25, eventIndex: 14 }, timestampMs: 1_505_000, killerParticipantId: 2, victimParticipantId: 7, assistingParticipantIds: [], position: { x: 4_500, y: 4_500 } },
    ],
    associatedObjectiveEvents: [],
  };
}

function representativeReview(): MatchReview {
  const dragon = { source: { frameIndex: 24, eventIndex: 30 }, timestampMs: 1_427_715, monsterType: "DRAGON", monsterSubType: "HEXTECH_DRAGON", killerParticipantId: 7, assistingParticipantIds: [8], position: { x: 9837, y: 4397 }, teamAttribution: { kind: "KnownTeam" as const, suppliedTeamId: 200, resolvedTeamId: 200, diagnosticReason: null } };
  const baron = { source: { frameIndex: 26, eventIndex: 7 }, timestampMs: 1_505_113, monsterType: "BARON_NASHOR", monsterSubType: null, killerParticipantId: 7, assistingParticipantIds: [8], position: { x: 5007, y: 10471 }, teamAttribution: { kind: "KnownTeam" as const, suppliedTeamId: 200, resolvedTeamId: 200, diagnosticReason: null } };
  const elementalFact = { id: "elemental-dragon.initial-spawn", objective: "elementalDragon" as const, initialSpawnTimestampMs: 300_000, sources: [{ title: "Riot objectives", url: "https://example.com/objectives" }] };
  const baronFact = { id: "baron-nashor.initial-spawn", objective: "baronNashor" as const, initialSpawnTimestampMs: 1_200_000, sources: [{ title: "Riot Baron", url: "https://example.com/baron" }] };
  return {
    matchId: "EUW1_1", mapId: 11, configuredParticipantId: 2,
    participants: [{ participantId: 2, championName: "Khazix", teamId: 100 }, { participantId: 7, championName: "Belveth", teamId: 200 }, { participantId: 8, championName: "Annie", teamId: 200 }],
    enemyResolution: { status: "resolved", participantId: 7 },
    versions: { reconstruction: 1, detector: 2, factualObservations: 1, knowledgeAnnotations: 1, encounters: 1 },
    knowledge: { publicPatch: "26.18", coverage: "available" }, sourceDataIssues: [],
    windows: [
      {
        requestedStartTimestampMs: 1_421_654, requestedEndTimestampMs: 1_620_500,
        startFrame: { frameIndex: 23, timestampMs: 1_380_440 }, endFrame: { frameIndex: 27, timestampMs: 1_620_500 },
        selectionRank: 1, primarySelectionReason: "goldAndXpChange", signalKinds: ["goldDifferenceChange", "xpDifferenceChange"], absorbedSignalKinds: ["configuredPlayerDeath"],
        observations: [
          { kind: "relativeGoldMovement", relativeStart: 4742, relativeEnd: 3182, signedChange: -1560, configuredPlayer: { startValue: 14650, endValue: 16243 }, enemyJungler: { startValue: 9908, endValue: 13061 } },
          { kind: "relativeXpMovement", relativeStart: -856, relativeEnd: -4182, signedChange: -3326, configuredPlayer: { startValue: 12401, endValue: 13501 }, enemyJungler: { startValue: 13257, endValue: 17683 } },
          { kind: "relativeJungleCsMovement", relativeStart: -4, relativeEnd: -25, signedChange: -21, configuredPlayer: { startValue: 154, endValue: 165 }, enemyJungler: { startValue: 158, endValue: 190 } },
          { kind: "configuredPlayerCombat", kills: 0, deaths: 2, assists: 1, distinctEventCount: 3, events: [{ source: { frameIndex: 24, eventIndex: 12 }, timestampMs: 1_425_000, killerParticipantId: 7, victimParticipantId: 2, assistingParticipantIds: [], position: null }] },
          { kind: "eliteObjectiveContext", events: [dragon, baron] },
        ],
        metricOmissions: [],
        positionSamples: [
          { frameIndex: 23, timestampMs: 1_380_440, configuredPlayerPosition: { x: 7443, y: 3036 }, enemyJunglerPosition: { x: 9665, y: 5278 } },
          { frameIndex: 24, timestampMs: 1_440_464, configuredPlayerPosition: { x: 463, y: 692 }, enemyJunglerPosition: { x: 11226, y: 6934 } },
        ],
        knowledgeAnnotations: [
          { kind: "recordedObjectiveContext", fact: elementalFact, target: { observationKind: "eliteObjectiveContext", event: dragon.source }, recordedKillTimestampMs: dragon.timestampMs },
          { kind: "recordedObjectiveContext", fact: baronFact, target: { observationKind: "eliteObjectiveContext", event: baron.source }, recordedKillTimestampMs: baron.timestampMs },
        ],
        encounters: [{
          id: "enc-v1-test", startTimestampMs: 1_425_000, endTimestampMs: 1_425_000, recordedEventSpanMs: 0,
          combatEventCount: 1, participantIds: [2, 7], distinctParticipantCount: 2,
          configuredPlayerSummary: { involved: true, kills: 0, deaths: 1, assists: 0, distinctEventCount: 1 }, enemyJunglerInvolved: true,
          combatEvents: [{ source: { frameIndex: 24, eventIndex: 12 }, timestampMs: 1_425_000, killerParticipantId: 7, victimParticipantId: 2, assistingParticipantIds: [], position: null }], associatedObjectiveEvents: [],
        }],
      },
      {
        requestedStartTimestampMs: 300_000, requestedEndTimestampMs: 360_000,
        startFrame: { frameIndex: 5, timestampMs: 300_000 }, endFrame: { frameIndex: 6, timestampMs: 360_000 },
        selectionRank: 2, primarySelectionReason: "configuredPlayerDeath", signalKinds: ["configuredPlayerDeath"], absorbedSignalKinds: [], observations: [], metricOmissions: [],
        positionSamples: [{ frameIndex: 5, timestampMs: 300_000, configuredPlayerPosition: null, enemyJunglerPosition: null }, { frameIndex: 6, timestampMs: 360_000, configuredPlayerPosition: null, enemyJunglerPosition: null }],
        knowledgeAnnotations: [{ kind: "nearInitialSpawn", fact: elementalFact, target: { observationKind: null, event: null }, recordedKillTimestampMs: null }], encounters: [],
      },
    ],
  };
}
