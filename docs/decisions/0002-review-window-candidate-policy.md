# ADR 0002: Review Window Candidate Policy

## Status

Accepted.

## Context

Pathwise can now reconstruct a stored jungle match deterministically and answer:

* `StateAt(T)`
* `Changes(T1, T2)`
* exact supported events between timestamps

The reconstruction layer intentionally describes factual game state and event history without judging decision quality.

The product's primary question remains:

> What should I have done differently?

Pathwise is not yet ready to answer that question directly.

Before recommendations or coaching interpretation can be trustworthy, Pathwise needs to reduce a full reconstructed match into a small number of periods that are worth inspecting.

A review-window candidate means:

> Something materially changed here, or explicit events make this period worth inspecting.

It does **not** mean:

> The player made a mistake here.

Pathwise also does not yet contain a formal League-of-Legends knowledge layer. Therefore the first detector must rely only on deterministic reconstruction data and explicit event/state changes rather than strategic conventions, matchup knowledge, patch-specific jungle heuristics, or coaching assumptions.

---

## Decision

Pathwise will introduce a deterministic, in-memory review-window detector above the reconstruction layer.

Version 1 will:

* generate candidate periods from explicit factual signals,
* merge and deduplicate overlapping candidates,
* select at most five periods,
* expose why each candidate was selected,
* preserve all supporting reconstruction evidence,
* make no claim about whether the period contains good or bad play.

The detector is an editorial selection policy over factual evidence, not a performance-scoring system.

The first version remains application/test-facing.

It will introduce no:

* REST endpoint,
* frontend feature,
* persistence,
* runtime dependency,
* LLM integration,
* coaching interpretation,
* League-knowledge framework.

---

## Relative State

Where opposing-jungler comparison is required, relative values are defined as:

```text
configured player − resolved enemy jungler
```

Examples:

```text
RelativeGold = PlayerTotalGold - EnemyTotalGold

RelativeXp = PlayerXp - EnemyXp
```

A positive value means the configured player is ahead in that observed metric.

A negative value means the opposing jungler is ahead.

If the opposing jungler is unresolved, relative metric signals are unavailable. Player-event and objective-based signals remain available.

---

## V1 Detection Signals

The first detector will use five signal types.

### Gold-difference change

Trigger when the absolute change in relative total gold reaches:

```text
1,000 gold
```

over an approximately three-minute observed interval.

This signal means only:

> The observed gold difference between the two junglers changed substantially.

It does not establish whether that change was strategically good, bad, expected, or deserved.

### XP-difference change

Trigger when the absolute change in relative XP reaches:

```text
1,500 XP
```

over the same approximately three-minute interval.

Gold and XP remain independent signals.

If both trigger on the same interval, they are represented as separate factual reasons within one seed.

### Configured-player death

Any exact champion-kill event in which the configured player is the victim produces a review-window seed.

A death is treated as worth inspection without assuming that the death was a mistake.

### Concentrated player combat

Trigger when at least:

```text
3 distinct champion-kill events
```

involving the configured player as:

* killer,
* victim,
* or assister

occur within:

```text
60 seconds
```

This represents temporal concentration of recorded combat outcomes.

It does not assert that the events were part of one fight.

### Elite-monster kill

Any mapped elite-monster kill produces a review-window seed.

No objective subtype receives strategic weighting in V1.

The event means only that an elite objective was taken during that period.

---

## Supporting Evidence Without Independent Triggers

The following information is exposed as supporting evidence but does not independently create a V1 candidate:

* level difference,
* jungle CS,
* lane CS,
* isolated kills,
* isolated assists,
* shutdown bounty values,
* structures,
* turret plates,
* item transactions,
* ward events,
* level-up events,
* dragon-soul markers,
* objective-bounty markers,
* game-end markers.

Gold/XP sign changes do not receive a separate "advantage reversal" trigger.

A small crossing around zero is not automatically more important than another measurable change.

Pathwise must preserve that gold, XP, level, and CS can tell different factual stories.

---

## Metric Seed Generation

Metric detection uses rolling comparisons between actual Riot timeline observations.

At each observed frame timestamp `end`:

1. Compute:

```text
end - 180,000 ms
```

2. Find the latest observed frame at or before that timestamp.
3. Skip the comparison if no such frame exists.
4. Compare the two actual observations.
5. Create a metric seed when the gold or XP threshold is reached.

Pathwise must not compare idealized clock-minute timestamps.

Actual source frame timestamps are authoritative.

Metric detection is skipped when the relevant interval crosses an adjacent frame gap greater than:

```text
90 seconds
```

Metric detection is also skipped when the corresponding cumulative counter regresses for either participant.

Skipped comparisons must remain visible as detector diagnostics.

The detector must not modify the underlying reconstruction values.

---

## Event Seed Generation

### Death and elite objective seeds

For configured-player deaths and elite-monster kills, use:

```text
event timestamp ± 60 seconds
```

Clamp resulting bounds to reconstruction coverage.

### Concentrated-combat seeds

For each configured-player-involved champion-kill event:

1. Inspect the closed 60-second lookback ending at that event.
2. Count distinct source champion-kill events involving the configured player.
3. If at least three exist:

   * begin 60 seconds before the earliest contributing event,
   * end 60 seconds after the latest contributing event.
4. Clamp to reconstruction coverage.

Trigger events must remain distinguishable from contextual events included inside the resulting review period.

The closed interval used to detect a combat cluster does not alter reconstruction's existing:

```text
(start, end]
```

query semantics.

---

## V1 Parameters

The initial immutable detector options are:

```text
Metric lookback:                 180 seconds
Gold-change threshold:          1,000
XP-change threshold:            1,500
Combat lookback:                60 seconds
Combat minimum:                 3 distinct events
Event context padding:          60 seconds each side
Maximum merged duration:        300 seconds
Duplicate overlap threshold:    50% of shorter window
Maximum selected windows:       5
Maximum adjacent frame gap:     90 seconds
```

These values are provisional selection parameters.

They are not universal truths about League of Legends.

The effective values must be included in detector output and behavior changes must be versioned.

They are not user-configurable in V1.

---

## Selection Priority

When more periods qualify than should be shown, use this explicit priority order:

1. Gold and XP thresholds both trigger on the same seed.
2. Gold threshold.
3. XP threshold.
4. Configured-player death.
5. Concentrated player combat.
6. Elite-monster kill.

This ordering is an editorial selection policy.

It does not mean that gold is strategically more important than XP, deaths, combat, or objectives.

### Tie-breaking

Within combined gold/XP and gold-only candidates:

* prefer the larger absolute gold change.

Within XP-only candidates:

* prefer the larger absolute XP change.

Within combat clusters:

* prefer more distinct contributing combat events.

Remaining ties are broken by:

1. earlier start,
2. earlier end,
3. stable source-reference ordering.

The detector may return fewer than two candidates when evidence is sparse.

It must never fabricate candidates merely to satisfy a target count.

---

## Merging and Deduplication

Candidate generation may create overlapping periods referring to the same underlying sequence.

Selection proceeds as follows:

1. Take the highest-priority remaining seed.
2. Scan remaining seeds in stable priority order.
3. Absorb a seed when:

   * its interval overlaps the selected interval by positive duration, and
   * the resulting union is no longer than 300 seconds.
4. Repeat until no additional qualifying seed can be absorbed.
5. Suppress remaining seeds whose overlap is at least 50% of the shorter interval.
6. Record which selected candidate suppressed each duplicate.
7. Continue until:

   * five candidates are selected, or
   * no seeds remain.
8. Return selected candidates chronologically while preserving their original selection rank.

Do not merge merely adjacent intervals.

Do not repeatedly add new context padding after merging.

Do not sum overlapping metric deltas.

Each original signal retains its own source interval and values.

Final-window changes are calculated independently from reconstruction.

A source event may appear as contextual evidence in neighboring candidates but may not be assigned as the same selection trigger twice.

---

## Review Window Evidence

Each selected candidate must expose enough information to explain exactly why it exists.

The candidate includes:

* configured-player identity,
* enemy-jungler identity when resolved,
* enemy-resolution status,
* requested window bounds,
* actual frame timestamps and indices used,
* event cutoff timestamps,
* `GameChanges` for the final selected interval,
* gold values and changes,
* XP values and changes,
* level values and changes,
* jungle-CS values and changes,
* lane-CS values and changes,
* K/D/A values and changes,
* relative endpoint values,
* relative signed changes,
* original trigger intervals,
* trigger thresholds and measured values,
* exact supporting events,
* reconstruction source references,
* source-data and attribution issues,
* selection rank,
* primary selection reason,
* absorbed signals,
* suppressed duplicate signals.

Arithmetic must remain inspectable.

The detector will not produce:

* confidence scores,
* skill scores,
* severity labels,
* weighted performance scores,
* opaque composite rankings.

---

## Domain and Application Boundary

Review-window detection belongs above deterministic reconstruction.

Conceptual flow:

```text
Stored Riot data
        ↓
Deterministic reconstruction
        ↓
Review-window candidate detection
        ↓
Future optional knowledge enrichment
        ↓
Future observation / interpretation
        ↓
Future recommendations
```

The Domain project will contain simple review-window concepts and a concrete pure detector.

Expected concepts include:

```text
ReviewWindowDetector
ReviewWindowOptions
ReviewSignal
ReviewWindowCandidate
ReviewWindowDetectionResult
```

The design should use:

* immutable records,
* a simple signal enum,
* direct inspectable arithmetic.

Do not introduce:

* a plugin system,
* generic rule engine,
* separate project,
* interface-per-rule abstraction.

The Application layer will expose a focused `ReviewWindowService` that obtains one reconstruction and invokes the detector.

No Infrastructure mapping changes are required.

---

## Explicit Non-Inferences

V1 must not infer:

* player mistakes,
* good decisions,
* bad decisions,
* correct contests,
* missed opportunities,
* expected jungle clears,
* camp availability,
* pathing quality,
* recall timing,
* lane priority,
* available player vision,
* player intent,
* mechanical execution quality,
* objective conversion,
* causation,
* responsibility,
* champion strength,
* item effectiveness,
* champion matchup state,
* scaling expectations,
* patch strategy,
* exact gold or XP at event timestamps between source frames.

An objective event proves only that the event occurred.

Even when team attribution is known, it does not imply that the configured player should have contested or prevented it.

---

## Future League-Knowledge Boundary

Pathwise does not yet contain a formal League-of-Legends knowledge layer.

Future enrichment may attach separately sourced/versioned context such as:

* known objective timing,
* common first-clear transitions,
* jungle-pathing conventions,
* lane-priority context,
* champion power spikes,
* matchup knowledge,
* patch-specific strategy.

Such enrichment must preserve the distinction between:

```text
reconstructed fact
```

and:

```text
League-specific interpretation
```

Knowledge annotations must carry:

* source/version,
* applicability,
* uncertainty where appropriate.

They must not overwrite the original factual reason that a review window was selected.

If future League knowledge affects which windows are selected, that behavior requires a separately versioned selection policy.

---

## First Regression Case

The existing sanitized Kha'Zix-versus-Bel'Veth fixture will serve as the first regression case for review-window detection.

Under the accepted V1 policy, the expected detector output is five candidate windows approximately covering:

```text
03:00.034 – 07:59.447
14:00.296 – 18:31.286
19:00.367 – 23:00.440
22:01.114 – 27:00.500
26:24.174 – 28:11.676
```

These are regression expectations produced by the approved factual policy.

They are not manually authored coaching conclusions.

The most significant late candidate demonstrates the detector's intended factual behavior: during the selected period, relative gold and XP change substantially, the configured player records several deaths, and elite objectives occur.

The detector must expose those facts without claiming why they happened.

---

## Calibration

This first fixture establishes feasibility, not general calibration.

Thresholds and merging behavior must later be evaluated across additional real games.

Future calibration may determine that parameters such as:

* 1,000 gold,
* 1,500 XP,
* 180-second lookback,
* 60-second combat clustering,
* 300-second maximum merged windows

should change.

Changing these values is an analysis-semantic change and must be deliberate and versioned.

Parameters should not be tuned merely to make one fixture produce aesthetically pleasing windows.

---

## Consequences

### Benefits

This policy:

* reduces a full match into a manageable review set,
* remains deterministic,
* preserves factual provenance,
* is explainable without opaque scoring,
* avoids premature coaching judgments,
* works without League-specific strategic assumptions,
* provides a stable input for future observation and recommendation layers,
* supports later game-knowledge enrichment without corrupting reconstructed facts.

### Limitations

V1 may:

* select periods that a human coach would not consider important,
* omit strategically significant periods without large factual changes,
* create relatively broad merged windows,
* be sensitive to provisional thresholds,
* miss context requiring League-specific game knowledge,
* provide fewer candidates in low-event games.

These are accepted limitations of the first deterministic candidate-selection layer.

---

## Guiding Rule

> Detect where the factual story changes before attempting to explain what the player should have done about it.
