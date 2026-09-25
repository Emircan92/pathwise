import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { MatchReviewContent, MatchReviewSection, formatMatchTime } from "./match-review-section";
import { getMatchReview, type MatchReview } from "@/lib/api/pathwise";
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

describe("match review presentation", () => {
  it("renders every observation and annotation variant chronologically with precise evidence", () => {
    const review = representativeReview();
    render(<MatchReviewContent review={review} />);

    expect(screen.getByRole("heading", { name: "5:00–6:00" })).toBeInTheDocument();
    expect(screen.getByText((_, element) => element?.tagName === "P" && element.textContent === "Initial-spawn context · Elemental Dragon initial spawn: 5:00")).toHaveClass("text-xs");
    fireEvent.change(screen.getByLabelText("Review period"), { target: { value: "1" } });
    const card = screen.getByRole("article");
    expect(within(card).getByRole("heading", { name: "23:41–27:00" })).toBeInTheDocument();
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
    expect(screen.getByText("Positions are periodic snapshots. Events use their recorded time and location. Movement between snapshots is unknown.")).toBeInTheDocument();

    const evidence = within(card).getByText("Evidence and sources").closest("details");
    expect(evidence).not.toHaveAttribute("open");
    fireEvent.click(within(card).getByText("Evidence and sources"));
    expect(evidence).toHaveAttribute("open");
    expect(screen.getByText("Review bounds: 23:41.654–27:00.500")).toBeInTheDocument();
    expect(screen.getByText("Metric frames: 23:00.440 (frame 23)–27:00.500 (frame 27)")).toBeInTheDocument();
    expect(screen.getByText("Signal types: Gold difference change, XP difference change")).toBeInTheDocument();
    expect(within(card).getByText("Events after the start, through the end.")).toBeInTheDocument();
    expect(within(card).getByRole("link", { name: "Riot objectives" })).toHaveAttribute("href", "https://example.com/objectives");

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
    expect(screen.getByLabelText("Enemy jungler")).toBeDisabled();
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

  it("keeps selected evidence, layer filtering, and boundary context in the primary review", () => {
    render(<MatchReviewContent review={representativeReview()} />);
    fireEvent.change(screen.getByLabelText("Review period"), { target: { value: "1" } });

    expect(screen.getByRole("heading", { name: "You died · 23:45.000" })).toBeInTheDocument();
    const boundaryMarker = screen.getByRole("button", { name: "You, 23:00.440, frame sample" });
    fireEvent.focus(boundaryMarker);
    expect(boundaryMarker).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByText("Before this period: sampled at 23:00.440. The period starts at 23:41.654.")).toBeInTheDocument();

    const dragonMarker = screen.getByRole("button", { name: "Hextech Dragon killed, 23:47.715, recorded event" });
    fireEvent.click(dragonMarker);
    expect(dragonMarker).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("heading", { name: "Hextech Dragon killed · 23:47.715" })).toBeInTheDocument();

    fireEvent.click(screen.getByLabelText("Objectives"));
    expect(screen.queryByRole("button", { name: "Hextech Dragon killed, 23:47.715, recorded event" })).not.toBeInTheDocument();
    expect(screen.getByText("Select an evidence item for details.")).toBeInTheDocument();
    fireEvent.click(screen.getByLabelText("You"));
    fireEvent.click(screen.getByLabelText("Enemy jungler"));
    fireEvent.click(screen.getByLabelText("Combat"));
    expect(screen.getByText("No evidence is visible with these layers.")).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Review period"), { target: { value: "0" } });
    expect(screen.getByLabelText("Objectives")).not.toBeChecked();
    expect(screen.getByText("No evidence is visible with these layers.")).toBeInTheDocument();
  });

  it("keeps missing locations in the rail and preserves evidence when artwork is unavailable", () => {
    const review = representativeReview();
    render(<MatchReviewContent review={review} />);
    fireEvent.change(screen.getByLabelText("Review period"), { target: { value: "1" } });
    expect(screen.getAllByText("Location not recorded").length).toBeGreaterThan(0);
    fireEvent.error(screen.getByAltText("Static Summoner's Rift reference artwork"));
    expect(screen.getByText("Map artwork could not load. Recorded evidence remains available in the timeline.")).toBeInTheDocument();
    expect(screen.getByText("Chronological evidence")).toBeInTheDocument();

    cleanup();
    render(<MatchReviewContent review={{ ...review, mapId: 12 }} />);
    expect(screen.getByText("Summoner's Rift artwork is unavailable for map 12. Recorded evidence remains below.")).toBeInTheDocument();
    expect(screen.getByText("Chronological evidence")).toBeInTheDocument();
  });

  it("keeps equal-time and overlapping evidence separate, then resets selection on period change", () => {
    const review = representativeReview();
    const period = review.windows[0];
    period.positionSamples[1] = { ...period.positionSamples[1], timestampMs: 1_427_715, configuredPlayerPosition: { x: 9837, y: 4397 }, enemyJunglerPosition: { x: 9837, y: 4397 } };
    const combat = period.observations.find((observation) => observation.kind === "configuredPlayerCombat");
    if (combat?.kind !== "configuredPlayerCombat") throw new Error("Test fixture lacks combat");
    combat.events[0] = { ...combat.events[0], timestampMs: 1_427_715, position: { x: 9837, y: 4397 } };
    period.encounters[0].combatEvents[0] = { ...period.encounters[0].combatEvents[0], timestampMs: 1_427_715, position: { x: 9837, y: 4397 } };
    const objectives = period.observations.find((observation) => observation.kind === "eliteObjectiveContext");
    if (objectives?.kind !== "eliteObjectiveContext") throw new Error("Test fixture lacks objectives");
    objectives.events.push({ ...objectives.events[0], source: { frameIndex: 24, eventIndex: 31 } });
    const simultaneous = buildSpatialEvidence(review, period).filter((entry) => entry.timestampMs === 1_427_715);
    expect(simultaneous.map((entry) => entry.layer)).toEqual(["you", "enemy", "combat", "objectives", "objectives"]);
    expect(new Set(simultaneous.map((entry) => entry.id)).size).toBe(5);

    render(<MatchReviewContent review={review} />);
    fireEvent.change(screen.getByLabelText("Review period"), { target: { value: "1" } });
    fireEvent.click(screen.getByRole("button", { name: "Baron Nashor killed, 25:05.113, recorded event" }));
    expect(screen.getByRole("heading", { name: "Baron Nashor killed · 25:05.113" })).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Review period"), { target: { value: "0" } });
    expect(screen.getByRole("heading", { name: "You · 6:00.000" })).toBeInTheDocument();
  });

  it("filters encounter evidence and samples, keeps toggles, and resets on period change", () => {
    const review = representativeReview();
    const period = review.windows[0];
    period.encounters[0].associatedObjectiveEvents = [period.observations.find((item) => item.kind === "eliteObjectiveContext")!.events[1]];
    const neutral = { source: { frameIndex: 24, eventIndex: 13 }, timestampMs: 1_500_000, killerParticipantId: 7, victimParticipantId: 8, assistingParticipantIds: [], position: { x: 4_000, y: 4_000 } };
    period.encounters.push({ id: "enc-v1-neutral", startTimestampMs: neutral.timestampMs, endTimestampMs: neutral.timestampMs,
      recordedEventSpanMs: 0, combatEventCount: 1, participantIds: [7, 8], distinctParticipantCount: 2,
      configuredPlayerSummary: { involved: false, kills: 0, deaths: 0, assists: 0, distinctEventCount: 0 },
      enemyJunglerInvolved: true, combatEvents: [neutral], associatedObjectiveEvents: [] });
    expect(buildSpatialEvidence(review, period).filter((entry) => entry.layer === "combat")).toHaveLength(2);
    expect(buildSpatialEvidence(review, period).find((entry) => entry.id.endsWith("combat:24:13"))?.label).toBe("Recorded champion kill");
    expect(buildSpatialEvidence(review, period, period.encounters[0]).filter((entry) => entry.layer === "combat")).toHaveLength(1);
    expect(buildSpatialEvidence(review, period, period.encounters[0]).filter((entry) => entry.layer === "objectives")).toHaveLength(1);
    expect(buildSpatialEvidence(review, period, period.encounters[0]).filter((entry) => entry.kind === "sample").map((entry) => entry.layer)).toEqual(["enemy"]);

    render(<MatchReviewContent review={review} />);
    fireEvent.change(screen.getByLabelText("Review period"), { target: { value: "1" } });
    fireEvent.click(screen.getByRole("button", { name: /23:45.000 · Single recorded kill/ }));
    expect(screen.getByRole("heading", { name: "You died · 23:45.000" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Recorded champion kill, 25:00.000/ })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /25:00.000 · Single recorded kill/ }));
    expect(screen.getByText(/No nearby frame samples/)).toBeInTheDocument();
    fireEvent.click(screen.getByLabelText("Combat"));
    fireEvent.click(screen.getByRole("button", { name: "Whole review period" }));
    expect(screen.getByLabelText("Combat")).not.toBeChecked();
    fireEvent.change(screen.getByLabelText("Review period"), { target: { value: "0" } });
    fireEvent.change(screen.getByLabelText("Review period"), { target: { value: "1" } });
    expect(screen.getByRole("button", { name: "Whole review period" })).toHaveAttribute("aria-pressed", "true");
  });

  it("uses closed nearby sample bounds without substituting an outside frame", () => {
    const review = representativeReview();
    const period = review.windows[0];
    period.encounters[0].combatEvents[0].victimParticipantId = 8;
    period.positionSamples = [1_421_653, 1_421_654, 1_455_000, 1_455_001].map((timestampMs, frameIndex) => ({
      frameIndex, timestampMs, configuredPlayerPosition: null, enemyJunglerPosition: null,
    }));
    const samples = buildSpatialEvidence(review, period, period.encounters[0]).filter((entry) => entry.kind === "sample");
    expect(samples.map((entry) => entry.timestampMs)).toEqual([1_421_654, 1_421_654, 1_455_000, 1_455_000]);
  });

  it("shows a recorded death location instead of a later base sample for the configured player", () => {
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
    expect(evidence.find((entry) => entry.layer === "combat")).toMatchObject({
      timestampMs: 1_425_000, position: { x: 9_000, y: 4_000 }, kind: "event", label: "You died",
    });
    expect(buildSpatialEvidence(review, period).filter((entry) => entry.layer === "you")).toHaveLength(4);

    render(<MatchReviewContent review={review} />);
    fireEvent.change(screen.getByLabelText("Review period"), { target: { value: "1" } });
    fireEvent.click(screen.getByRole("button", { name: /23:45.000 · Single recorded kill/ }));
    expect(screen.getByRole("button", { name: "You died, 23:45.000, recorded event" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Nearby frame sample · You, 24:00.000, frame sample" })).not.toBeInTheDocument();
  });

  it("applies the death cutoff independently to the enemy jungler across multiple events", () => {
    const review = representativeReview();
    const period = review.windows[0];
    const encounter = period.encounters[0];
    encounter.combatEvents = [
      { ...encounter.combatEvents[0], victimParticipantId: 8, timestampMs: 1_424_000 },
      { ...encounter.combatEvents[0], source: { frameIndex: 24, eventIndex: 13 }, victimParticipantId: 7, timestampMs: 1_430_000, position: { x: 7_000, y: 5_000 } },
      { ...encounter.combatEvents[0], source: { frameIndex: 24, eventIndex: 14 }, victimParticipantId: 2, timestampMs: 1_445_000 },
    ];
    period.positionSamples = [1_425_000, 1_430_000, 1_435_000, 1_450_000].map((timestampMs, frameIndex) => ({
      frameIndex, timestampMs, configuredPlayerPosition: { x: 8_000, y: 4_000 }, enemyJunglerPosition: { x: 7_000, y: 5_000 },
    }));

    const evidence = buildSpatialEvidence(review, period, encounter);
    expect(evidence.filter((entry) => entry.layer === "enemy").map((entry) => entry.timestampMs)).toEqual([1_425_000]);
    expect(evidence.filter((entry) => entry.layer === "you").map((entry) => entry.timestampMs)).toEqual([1_425_000, 1_430_000, 1_435_000]);
    expect(evidence.find((entry) => entry.layer === "combat" && entry.eventIndex === 13)).toMatchObject({
      kind: "event", position: { x: 7_000, y: 5_000 },
    });

    review.enemyResolution = { status: "ambiguous", participantId: null };
    expect(buildSpatialEvidence(review, period, encounter).filter((entry) => entry.layer === "enemy")).toHaveLength(0);
  });

  it("leaves the victim location unavailable when the death has no position and only later frames exist", () => {
    const review = representativeReview();
    const period = review.windows[0];
    period.positionSamples = [{ frameIndex: 24, timestampMs: 1_440_000,
      configuredPlayerPosition: { x: 463, y: 692 }, enemyJunglerPosition: { x: 11_000, y: 7_000 } }];

    const evidence = buildSpatialEvidence(review, period, period.encounters[0]);
    expect(evidence.filter((entry) => entry.layer === "you")).toHaveLength(0);
    expect(evidence.find((entry) => entry.layer === "combat")).toMatchObject({ position: null, kind: "event" });
    expect(evidence.filter((entry) => entry.layer === "enemy")).toHaveLength(1);
  });

  it("does not call simultaneous distinct kill events a singleton", () => {
    const review = representativeReview();
    const encounter = review.windows[0].encounters[0];
    encounter.combatEvents.push({ ...encounter.combatEvents[0], source: { frameIndex: 24, eventIndex: 14 } });
    encounter.combatEventCount = 2;
    render(<MatchReviewContent review={review} />);
    fireEvent.change(screen.getByLabelText("Review period"), { target: { value: "1" } });
    expect(screen.queryByRole("button", { name: /Single recorded kill/ })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /2 kill events/ })).toBeInTheDocument();
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
    expect(await screen.findByRole("heading", { name: "5:00–6:00" })).toBeInTheDocument();
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
  const dragon = { source: { frameIndex: 24, eventIndex: 30 }, timestampMs: 1_427_715, monsterType: "DRAGON", monsterSubType: "HEXTECH_DRAGON", killerParticipantId: 7, assistingParticipantIds: [8], position: { x: 9837, y: 4397 }, teamAttribution: { kind: "KnownTeam" as const, suppliedTeamId: 200, resolvedTeamId: 200, diagnosticReason: null } };
  const baron = { source: { frameIndex: 26, eventIndex: 7 }, timestampMs: 1_505_113, monsterType: "BARON_NASHOR", monsterSubType: null, killerParticipantId: 7, assistingParticipantIds: [8], position: { x: 5007, y: 10471 }, teamAttribution: { kind: "KnownTeam" as const, suppliedTeamId: 200, resolvedTeamId: 200, diagnosticReason: null } };
  const elementalFact = { id: "elemental-dragon.initial-spawn", objective: "elementalDragon" as const, initialSpawnTimestampMs: 300_000, sources: [{ title: "Riot objectives", url: "https://example.com/objectives" }] };
  const baronFact = { id: "baron-nashor.initial-spawn", objective: "baronNashor" as const, initialSpawnTimestampMs: 1_200_000, sources: [{ title: "Riot Baron", url: "https://example.com/baron" }] };
  return {
    matchId: "EUW1_1",
    mapId: 11,
    configuredParticipantId: 2,
    participants: [{ participantId: 2, championName: "Khazix", teamId: 100 }, { participantId: 7, championName: "Belveth", teamId: 200 }, { participantId: 8, championName: "Annie", teamId: 200 }],
    enemyResolution: { status: "resolved", participantId: 7 },
    versions: { reconstruction: 1, detector: 2, factualObservations: 1, knowledgeAnnotations: 1, encounters: 1 },
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
        encounters: [{ id: "enc-v1-test", startTimestampMs: 1_425_000, endTimestampMs: 1_425_000, recordedEventSpanMs: 0,
          combatEventCount: 1, participantIds: [2, 7], distinctParticipantCount: 2,
          configuredPlayerSummary: { involved: true, kills: 0, deaths: 1, assists: 0, distinctEventCount: 1 },
          enemyJunglerInvolved: true,
          combatEvents: [{ source: { frameIndex: 24, eventIndex: 12 }, timestampMs: 1_425_000, killerParticipantId: 7, victimParticipantId: 2, assistingParticipantIds: [], position: null }],
          associatedObjectiveEvents: [] }],
      },
      {
        requestedStartTimestampMs: 300_000, requestedEndTimestampMs: 360_000,
        startFrame: { frameIndex: 5, timestampMs: 300_000 }, endFrame: { frameIndex: 6, timestampMs: 360_000 },
        selectionRank: 2, primarySelectionReason: "configuredPlayerDeath", signalKinds: ["configuredPlayerDeath"], absorbedSignalKinds: [], observations: [], metricOmissions: [],
        positionSamples: [{ frameIndex: 5, timestampMs: 300_000, configuredPlayerPosition: null, enemyJunglerPosition: null }, { frameIndex: 6, timestampMs: 360_000, configuredPlayerPosition: null, enemyJunglerPosition: null }],
        knowledgeAnnotations: [{ kind: "nearInitialSpawn", fact: elementalFact, target: { observationKind: null, event: null }, recordedKillTimestampMs: null }],
        encounters: [],
      },
    ],
  };
}
