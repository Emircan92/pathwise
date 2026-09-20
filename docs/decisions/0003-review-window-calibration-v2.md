# ADR 0003: Review Window Calibration V2

## Status

Accepted.

Supersedes selected policy details from ADR 0002: Review Window Candidate Policy.

ADR 0002 remains the historical record of the original V1 policy and rationale.

---

## Context

Pathwise V1 review-window detection was implemented as a deterministic factual-selection layer above game reconstruction.

Its initial policy used:

* gold-difference change of at least 1,000,
* XP-difference change of at least 1,500,
* configured-player deaths,
* concentrated player combat,
* elite-monster kills,
* 180-second metric lookback,
* 60-second event padding,
* 300-second maximum merged duration,
* 50% duplicate-overlap suppression,
* maximum five selected windows.

A read-only calibration pass was run over ten real stored Ranked Solo jungle games.

The V1 detector behaved correctly and deterministically, but the sample showed that selection was too broad and too saturated:

* all 10 games returned the maximum five windows,
* 8/10 games still had additional candidates excluded by the cap,
* average selected-window duration was approximately 3:50,
* median duration was approximately 4:03,
* 40% of selected windows lasted between 4:30 and 5:00,
* every sampled game contained overlapping selected windows,
* elite-monster events produced substantial seed pressure,
* metric-selected windows frequently expanded into broad containers for nearby event signals.

The calibration did not show a clear problem with the existing 1,000-gold or 1,500-XP thresholds.

A second read-only simulation evaluated narrowly scoped policy changes against the same sample.

The revised policy substantially improved window sharpness and distinctness without changing reconstruction semantics or introducing League-specific strategic interpretation.

---

## Decision

Review-window detector behavior will advance from detector version 1 to detector version 2.

Version 2 makes exactly three behavioral changes:

1. Elite objectives become supporting context only.
2. Maximum merged duration changes from 300 seconds to 210 seconds.
3. Duplicate-overlap suppression changes from 50% to 30% of the shorter window.

The maximum selected-window count remains five.

Gold and XP thresholds remain unchanged.

The remaining trigger types, priority policy, reconstruction semantics, and factual-only interpretation remain unchanged.

---

## Elite Objectives Become Context Only

Elite-monster events no longer independently create review-window seeds.

They remain:

* fully represented in reconstruction,
* available in `SupportingEvents`,
* preserved with source timestamp,
* preserved with participant/team attribution,
* preserved with source references.

An elite objective can appear inside a selected review window when that interval was independently selected by another qualifying signal.

Elite objectives must not:

* create a standalone candidate,
* extend candidate bounds,
* bridge two candidates during merging,
* affect selection priority,
* act as a selection trigger.

This change means that an isolated Dragon, Herald, Baron, Horde, or other elite-monster event may exist without Pathwise selecting a review window around it.

That is intentional.

The event establishes only that an objective occurred. It does not, by itself, establish that the period deserves one of the limited review slots.

No objective subtype or team receives strategic weighting.

---

## Maximum Merged Duration

The maximum permitted union when absorbing an overlapping seed changes from:

```text
300,000 ms
```

to:

```text
210,000 ms
```

The rule remains:

> An overlapping seed may be absorbed only when the resulting merged union remains within the configured maximum merged duration.

This value is a merge constraint, not a hard maximum duration for every selected window.

Original metric seeds may exceed 210 seconds because reconstruction uses actual source-frame timestamps rather than idealized clock times.

Such original seeds must remain intact.

Pathwise must not:

* clip them,
* interpolate them,
* shorten them,
* discard them merely because they exceed the merge limit.

The 210-second value was selected because calibration showed that:

* 300 seconds permitted overly broad merged periods,
* 240 seconds remained broader than desired,
* 180 seconds was too restrictive because ordinary approximately-three-minute metric intervals can slightly exceed 180 seconds due to real frame timestamp drift,
* 210 seconds preserves modest context while materially improving sharpness.

---

## Duplicate Overlap Suppression

The duplicate-overlap threshold changes from:

```text
50% of the shorter window
```

to:

```text
30% of the shorter window
```

A remaining candidate is suppressed when:

```text
positive overlap duration >= 0.30 × shorter window duration
```

This threshold also applies to the existing guard preventing later candidate expansion from becoming substantially duplicative of a previously selected window.

The calibration showed that rolling approximately-three-minute metric intervals offset in time often overlap by roughly one-third.

The previous 50% rule frequently retained both.

The 30% rule more effectively removes neighboring windows that describe substantially the same factual period.

Limited overlap remains allowed.

Merely adjacent intervals remain distinct.

Suppression must continue to preserve:

* original source interval,
* original trigger evidence,
* suppression reason,
* selected candidate responsible for suppression.

A suppressed signal must not be falsely described as absorbed into a selected window when its factual interval extends outside that selected window.

---

## Detector Version

The review-window detector version changes from:

```text
1
```

to:

```text
2
```

This is required because the same reconstructed game may now produce different review-window output under the revised selection semantics.

Detector output must continue to expose:

* detector version,
* reconstruction version,
* effective options,
* source issues,
* skipped-comparison diagnostics,
* suppression diagnostics,
* selection provenance.

---

## Rules That Remain Unchanged

Version 2 retains the following behavior from ADR 0002.

### Reconstruction semantics

No changes to:

* `StateAt(T)`,
* `Changes(T1,T2)`,
* source-frame precision,
* `(start, end]` event semantics,
* event provenance,
* unresolved attribution behavior.

### Relative metrics

Relative values remain:

```text
configured player − resolved enemy jungler
```

### Metric detection

The following remain unchanged:

```text
Metric lookback:               180,000 ms
Gold-change threshold:        1,000
XP-change threshold:          1,500
Maximum adjacent frame gap:   90,000 ms
```

Threshold comparisons remain inclusive and symmetric.

Gold and XP remain independent signals.

There is no separate sign-crossing or generic advantage-reversal trigger.

### Configured-player deaths

Every configured-player death remains a qualifying event seed.

### Concentrated combat

Combat clustering remains:

```text
3 distinct player-involved champion-kill events
within a closed 60,000 ms lookback
```

### Event padding

Death and combat event context remains:

```text
60,000 ms per side
```

clamped to reconstruction coverage.

### Selection ceiling

The detector still returns:

```text
up to 5 review windows
```

There is no minimum.

The detector must never fabricate windows to satisfy a target count.

### Selection priority

The active priority becomes:

1. Gold and XP triggering on the same original seed.
2. Gold.
3. XP.
4. Configured-player death.
5. Concentrated player combat.

Elite objectives are removed from active priority because they no longer generate seeds.

Existing tie-breaking rules remain unchanged.

### Merging

The following remain unchanged:

* only positive-overlap candidates can merge,
* repeated merge scans remain supported,
* merely adjacent windows do not merge,
* context padding is not reapplied after merging,
* overlapping metric deltas are not summed,
* original trigger intervals remain preserved,
* final-window `GameChanges` is reconstructed independently,
* trigger ownership remains unique.

---

## Calibration Results

Using the same ten-game sample, the simulated Version 2 policy changed aggregate behavior approximately as follows:

```text
Current detector V1
Selected windows:              50
Games returning 5 windows:     10 / 10
Mean duration:                 ~3:50
Median duration:               ~4:03
Windows lasting 4:30–5:00:    20 / 50
Games with selected overlap:   10 / 10
Overlapping selected pairs:    15
Objective-primary windows:     4
```

Version 2 simulation:

```text
Selected windows:              46
Games returning 5 windows:     7 / 10
Mean duration:                 ~2:57
Median duration:               ~3:00
Windows lasting 4:30–5:00:    0 / 46
Games with selected overlap:   3 / 10
Overlapping selected pairs:    3
Objective-primary windows:     0
```

Window-count distribution under the sample becomes:

```text
1 game  → 3 windows
2 games → 4 windows
7 games → 5 windows
```

This remains heavier than the eventual desired product shape.

The sample does not establish enough quiet-game behavior to justify additional filtering or a lower hard cap.

Version 2 therefore intentionally stops after these three evidence-supported changes.

---

## Regression Case

The sanitized Kha'Zix-versus-Bel'Veth fixture remains the primary regression case.

Under detector version 2, expected selected periods are approximately:

```text
03:00.034 – 06:00.067
15:00.315 – 18:05.356
20:00.393 – 23:00.440
23:41.654 – 27:00.500
26:57.047 – 28:11.676
```

Representative behavior:

* the first window remains selected by the original +1,165 relative-gold change,
* the large late-game window remains selected by combined gold and XP divergence,
* Dragon and Baron remain visible as context inside that late-game window,
* the final death remains independently reviewable,
* the earlier death/combat sequence no longer expands the first gold window toward eight minutes.

These expectations remain factual detector outputs, not coaching conclusions.

---

## Explicit Non-Inferences

Version 2 does not introduce any ability to judge:

* mistakes,
* good or bad decisions,
* missed opportunities,
* correct objective contests,
* expected jungle pathing,
* recall quality,
* lane priority,
* champion strength,
* item effectiveness,
* scaling,
* matchup state,
* patch strategy,
* player intent,
* mechanical execution,
* objective conversion,
* causal responsibility.

Elite objectives becoming context-only must not be interpreted as meaning objectives are strategically unimportant.

It means only that an objective event alone is not sufficient evidence to consume a V2 review slot.

---

## Future Calibration

Version 2 remains provisional.

Future calibration should include:

* additional games,
* quieter matches,
* lower-combat games,
* stomps,
* games played from behind,
* farm-heavy games,
* unresolved-enemy scenarios where available.

Future evidence may justify revisiting:

* metric thresholds,
* review-window cap,
* combat clustering,
* merge limit,
* duplicate suppression,
* signal eligibility.

Such changes remain core analysis-semantic decisions and must be explicit and versioned.

Parameters must not be tuned merely to make individual fixture outputs aesthetically pleasing.

---

## Consequences

### Benefits

Version 2:

* reduces overly broad candidate periods,
* substantially reduces redundant selected overlap,
* removes objective-driven seed pressure,
* retains objective events as factual context,
* preserves deterministic provenance,
* keeps policy inspectable,
* introduces no League-specific strategic assumptions,
* requires no reconstruction, persistence, REST, frontend, or dependency changes.

### Limitations

Version 2 still:

* frequently returns five candidates in the current sample,
* has not been validated against enough quiet games,
* may omit strategically important objective-only periods,
* remains dependent on provisional factual thresholds,
* cannot identify strategically important periods that lack supported factual triggers.

These are accepted calibration limitations.

---

## Guiding Rule

> Make review attention sharper without pretending to know what good League of Legends should look like.
