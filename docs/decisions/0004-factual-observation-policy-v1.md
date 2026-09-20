# ADR 0004: Factual Observation Policy V1

## Status

Accepted.

This ADR establishes the first deterministic factual-observation layer in Pathwise.

It also clarifies the analysis pipeline ordering as:

```text
reconstruction
→ review-window selection
→ factual observations
→ future League/game knowledge enrichment
→ future recommendations
→ future AI explanation
```

This supersedes any earlier conceptual ordering that placed knowledge enrichment before factual observations.

---

## Context

Pathwise currently has:

* raw Riot Match-V5 and Timeline-V5 persistence,
* deterministic game reconstruction,
* `StateAt(T)`,
* `Changes(T1,T2)`,
* calibrated Version 2 review-window detection.

Review-window detection identifies a small set of factual periods worth inspecting.

However, a selected review window still exposes mostly low-level reconstructed state, deltas, and events.

The next layer should transform those facts into a small, typed, deterministic set of observations that can be consumed by:

* future UI,
* future League/game knowledge enrichment,
* future recommendation logic,
* future AI explanation.

The observation layer must not judge play.

It must describe what was observed.

---

## Decision

Add a deterministic factual-observation generator in the Domain layer.

The generator operates only on final selected review windows.

For each selected window, it may emit at most one observation in each of five categories:

1. Relative gold movement.
2. Relative XP movement.
3. Relative Jungle CS movement.
4. Configured-player combat summary.
5. Elite-objective context.

The generator returns structured Domain objects only.

It does not produce prose, severity, confidence, recommendations, strategic interpretation, or League-specific judgment.

---

## Observation Categories

### Relative Gold Movement

Compare the configured player's total gold with the resolved enemy jungler.

For the selected window:

```text
relativeStart = playerStart - enemyStart
relativeEnd   = playerEnd - enemyEnd
signedChange  = relativeEnd - relativeStart
```

Emit a gold observation when:

```text
abs(signedChange) >= 500
```

The threshold is inclusive.

---

### Relative XP Movement

Compare configured-player XP with enemy-jungler XP using the same endpoint arithmetic.

Emit when:

```text
abs(signedChange) >= 750
```

The threshold is inclusive.

---

### Relative Jungle CS Movement

Compare reconstructed `jungleMinionsKilled` values.

Emit when:

```text
abs(signedChange) >= 10
```

The threshold is inclusive.

The observation is explicitly named Jungle CS.

It must not be described as:

* camps cleared,
* camp advantage,
* pathing,
* farming efficiency.

Riot exposes Jungle CS, not deterministic camp-clear history.

---

### Configured-Player Combat Summary

Emit one combat observation when at least one distinct champion-kill event in the selected interval involves the configured player as:

* killer,
* victim,
* assister.

The observation retains all matching distinct source events and derives:

* kill count,
* death count,
* assist count,
* distinct combat-event count.

Deaths with unknown killers remain valid events.

Duplicate assisting-participant IDs must not multiply assist count.

This category intentionally summarizes kills, deaths, and assists together rather than creating separate observation categories.

---

### Elite-Objective Context

Emit one elite-objective context observation when at least one `EliteMonsterKillEvent` exists in the selected interval.

Preserve:

* timestamp,
* source event reference,
* monster type,
* subtype,
* existing participant/team attribution.

Do not infer:

* objective participation,
* contest quality,
* objective value,
* causal relationships,
* strategic correctness.

Objectives are factual context only.

Soul markers and objective-bounty markers are not additional elite-monster kills.

---

## Supporting Evidence Only

The following remain available through reconstruction or review-window evidence but are not separate V1 observations:

* relative level,
* structure events,
* item transactions,
* endpoint positions,
* source issues,
* detector triggers,
* detector suppression diagnostics.

Relative level is intentionally not promoted because V1 already reports XP progression.

Position movement is deferred because sparse snapshots do not establish continuous paths.

Health/damage observations are deferred because current reconstruction does not provide sufficient encounter context.

---

## Relative Observation Rules

Gold, XP, and Jungle CS observations require a resolved enemy jungler.

If enemy-jungler resolution is missing or ambiguous:

* relative gold is omitted,
* relative XP is omitted,
* relative Jungle CS is omitted.

Combat and objective observations remain eligible.

No configured-player-only substitute is silently generated.

---

## Final-Window Evidence

Metric observations use the final selected review window's reconstructed:

* `Changes.StartState`,
* `Changes.EndState`.

They must not use:

* original trigger values,
* absorbed-seed values,
* summed detector deltas,
* strongest internal subintervals.

The observation layer describes the final review interval.

The detector explains why that interval was selected.

Those are different responsibilities.

---

## Metric Eligibility Thresholds

Version 1 uses:

```text
Relative gold:       500
Relative XP:         750
Relative Jungle CS:  10
```

These are observation-presentation thresholds.

They are independent of review-window detector thresholds.

They are not assertions of strategic significance.

They exist only to suppress low-value factual noise.

The thresholds are symmetric for positive and negative movement.

There is no:

* percentage threshold,
* duration normalization,
* sign-crossing bonus,
* positive/negative preference.

Zero change never qualifies.

---

## Data-Quality Handling

For each supported metric, inspect reconstructed frames spanning the selected starting source frame through the ending source frame.

If either participant's cumulative counter for that metric decreases between adjacent observations:

* omit that metric observation,
* record a metric-observation omission,
* preserve evidence for the first regression.

A regression in one metric does not suppress unrelated observations.

The observation layer does not copy the detector's 90-second frame-gap rule.

Metric observations are endpoint comparisons, not claims of continuous trajectory or rate.

Actual frame timestamps remain visible in provenance.

---

## Event Interval Semantics

Combat and objective observations use the selected window's existing event interval semantics:

```text
(start, end]
```

Events exactly at the start are excluded.

Events exactly at the end are included.

Distinct events are deduplicated by source event reference, not timestamp or payload contents.

Simultaneous distinct events remain distinct.

---

## Reduction and Ordering

V1 has one observation slot per category.

Eligible observations are returned in fixed order:

1. Relative gold.
2. Relative XP.
3. Relative Jungle CS.
4. Configured-player combat.
5. Elite-objective context.

This order is for deterministic presentation.

It is not strategic priority.

Because there is at most one observation per category, V1 naturally returns zero to five observations without another scoring or truncation algorithm.

Combat and objective summaries do not compete with metric observations for capacity.

---

## Domain Contract

Add the observation models and generator under:

```text
Pathwise.Domain.FactualObservations
```

The model should use typed records rather than generic property bags.

The core concepts are:

* `FactualObservationKind`
* `SelectedReviewWindowKey`
* `FactualObservationKey`
* `ParticipantMetricEndpoints`
* `RelativeMetricEvidence`
* abstract `FactualObservation`
* `RelativeGoldObservation`
* `RelativeXpObservation`
* `RelativeJungleCsObservation`
* `ConfiguredPlayerCombatObservation`
* `EliteObjectiveContextObservation`
* `FactualObservationPolicy`
* `CounterRegressionEvidence`
* `MetricObservationOmission`
* `WindowFactualObservations`
* `FactualObservationResult`

The public generator entry point is:

```csharp
FactualObservationResult Generate(
    GameReconstruction reconstruction,
    ReviewWindowDetectionResult selectedWindows);
```

Generator Version 1 uses a fixed policy:

```text
Gold:      500
XP:        750
Jungle CS: 10
```

V1 does not expose a caller-configurable observation policy.

---

## Identity

A selected review window is identified by:

* match ID,
* configured participant ID,
* optional enemy participant ID,
* exact requested start timestamp,
* exact requested end timestamp,
* reconstruction version,
* detector version.

The identity must not depend on:

* display order,
* selection rank,
* list index.

An observation key adds:

* observation-generator version,
* observation kind.

This identity is valid because V1 guarantees at most one observation of each kind per selected window.

If future versions allow multiple observations of the same kind within a window, the observation-key design must be revisited.

---

## Provenance

Every observation must remain traceable to reconstruction evidence.

Metric observations retain:

* requested selected-window bounds,
* actual source start frame,
* actual source end frame,
* participant IDs,
* player absolute start/end values,
* enemy absolute start/end values,
* reconstruction version,
* detector version,
* generator version.

Event observations retain their existing typed events and source references.

The result also retains:

* enemy-jungler resolution,
* source issues,
* effective observation policy,
* observation-generator version.

Raw Riot JSON is not duplicated.

---

## Wording Boundary

The Domain observation layer produces structured facts only.

It must not emit human-readable coaching or evaluative prose.

A future deterministic presentation component may render wording such as:

```text
Relative gold moved from +4,742 to +3,182.
```

But Domain must not generate statements such as:

```text
You lost a large gold advantage.
You farmed poorly.
You should have backed.
This was a bad Baron contest.
```

Structured observations must remain useful without a frontend or LLM.

---

## Application Composition

Add a focused `MatchReviewService`.

The flow is:

```text
MatchReviewService.GetAsync(...)
    → ReviewWindowService.GetAsync(...)
    → existing reconstruction
    → existing ReviewWindowDetector V2
    → FactualObservationGenerator.Generate(...)
```

Application returns a `MatchReview` containing:

* review-window detection result,
* factual-observation result.

Existing reconstruction/source metadata and failures remain intact.

Do not:

* reload the match,
* reconstruct twice,
* redetect windows,
* alter `ReviewWindowService`.

Generate observations only for final selected review windows.

No observations are generated for:

* suppressed candidates,
* absorbed source seeds,
* cap-excluded seeds.

---

## Future League-Knowledge Boundary

Future knowledge enrichment must sit above immutable factual observations.

Conceptually:

```text
FactualObservation
    → KnowledgeAnnotation
    → Recommendation
```

Future annotations may reference a versioned `FactualObservationKey`.

They must carry their own:

* source,
* version,
* applicability,
* uncertainty.

Knowledge annotations must not modify:

* observed values,
* observation eligibility,
* factual provenance,
* observation ordering.

Changing factual-generation semantics requires a new observation-generator version.

No knowledge-annotation framework is implemented by this ADR.

---

## Explicit Non-Inferences

Factual Observation V1 does not determine:

* mistakes,
* good decisions,
* bad decisions,
* alternatives,
* missed opportunities,
* correct objective contests,
* objective value,
* pathing quality,
* recall quality,
* camps cleared,
* camps available,
* lane priority,
* vision state,
* player intent,
* mechanical execution,
* champion strength,
* matchup state,
* scaling,
* item effectiveness,
* patch strategy,
* causal responsibility,
* whether one event caused another.

It also does not claim continuous movement or continuous metric trajectories between Riot frames.

---

## Regression Fixture

Use the existing sanitized Kha'Zix-versus-Bel'Veth fixture.

For selected interval:

```text
23:41.654 – 27:00.500
```

metric evidence uses:

```text
Start frame:
23:00.440

End frame:
27:00.500
```

Expected observations, in order:

### Relative Gold

```text
Player:   14,650 → 16,243
Enemy:     9,908 → 13,061

Relative:
+4,742 → +3,182

Signed change:
-1,560
```

### Relative XP

```text
Player:   12,401 → 13,501
Enemy:    13,257 → 17,683

Relative:
-856 → -4,182

Signed change:
-3,326
```

### Relative Jungle CS

```text
Player:   154 → 165
Enemy:    158 → 190

Relative:
-4 → -25

Signed change:
-21
```

### Configured-Player Combat

```text
0 kills
2 deaths
1 assist
3 distinct recorded events
```

### Elite-Objective Context

```text
Hextech Dragon
Baron Nashor
```

Both remain attributed using existing reconstructed evidence.

Relative level changes from -1 to -2 but remains supporting evidence only.

These are selected-window facts.

They must not be replaced with the detector's original combined trigger evidence.

---

## Validation Requirements

### Domain Tests

Cover:

* gold thresholds at 499 / 500 / 501,
* XP thresholds at 749 / 750 / 751,
* Jungle CS thresholds at 9 / 10 / 11,
* positive and negative boundaries,
* zero/no-change behavior,
* equal relative movement despite absolute gains,
* small sign crossings,
* unresolved enemy behavior,
* ambiguous enemy behavior,
* multiple kills/deaths/assists,
* unknown killers,
* duplicate assist IDs,
* simultaneous distinct combat events,
* `(start, end]` boundaries,
* objective-only context,
* objective attribution,
* metric counter regressions,
* sparse frames,
* fixed observation ordering,
* repeatable keys,
* overlapping selected windows,
* generator version,
* effective policy.

### Fixture Integration

Validate:

```text
fixture JSON
→ mapper
→ reconstruction
→ detector V2
→ observation generator V1
```

Assert:

* exact five-observation set,
* arithmetic,
* source frames,
* event references,
* attribution,
* ordering.

Existing detector regression assertions remain unchanged.

### Application Tests

Verify:

* one source load,
* unchanged reconstruction/source metadata,
* unchanged detector output,
* no write behavior,
* no API-key requirement,
* failure propagation,
* cancellation propagation.

---

## Consequences

### Benefits

This layer gives Pathwise a compact factual vocabulary for selected review periods.

It:

* reduces raw evidence into human-usable structure,
* remains deterministic,
* preserves provenance,
* avoids League-specific assumptions,
* gives future knowledge/recommendation layers a stable input,
* remains testable without UI or LLM integration.

### Limitations

V1:

* uses provisional presentation thresholds,
* reports only five observation categories,
* does not judge importance within a selected window,
* does not summarize structures/items/position/damage,
* cannot produce relative metrics when enemy jungler is unresolved,
* intentionally omits small factual changes.

These limitations are accepted for V1.

---

## Guiding Rule

> Describe what changed before attempting to explain what it meant.
