# ADR 0005: Game Reconstruction Model

## Status

Accepted.

## Context

Pathwise needs to reconstruct stored League of Legends matches over time so that future analysis can answer questions such as:

* What was the configured player's state at time T?
* What was the opposing jungler's state at time T?
* What changed between T1 and T2?
* Which exact events occurred during that interval?

Reconnaissance against real stored Riot Match-V5 and Timeline-V5 payloads established that:

* timeline participant frames occur approximately once per minute,
* each frame provides participant state such as position, gold, XP, level, lane CS, jungle CS, health/resource state, champion stats, and cumulative damage statistics,
* precise discrete events exist between frames for champion kills, elite monsters, structures, item transactions, level-ups, wards, and other event types,
* normal jungle camp kills and respawn state are not available,
* recall events are not available,
* continuous movement paths are not available,
* inventory-at-time would require reconstructing item transaction semantics.

Pathwise should therefore distinguish between:

1. state observed directly at discrete Riot timeline frames,
2. precise events occurring between frames,
3. deterministic values derived from those events,
4. facts that cannot be reconstructed reliably.

The first reconstruction slice should remain deterministic and small. It must not prematurely introduce analytics, inferred pathing, or recommendation logic.

---

## Decision

Pathwise will reconstruct a match from retained raw Riot timeline data using frame-backed state and precise event replay.

Reconstruction results will preserve source timestamps and will not fabricate intermediate state through interpolation.

---

## StateAt(T)

`StateAt(T)` returns the most recent Riot timeline frame whose timestamp is less than or equal to `T`.

Example:

```text
Requested time: 15:37
Available frames:
15:00
16:00

StateAt(15:37) uses the 15:00 frame.
```

The returned state must expose the actual frame timestamp used.

Pathwise must not interpolate:

* position,
* total gold,
* current gold,
* XP,
* level,
* lane CS,
* jungle CS,
* health,
* resource values,
* or other frame counters.

Precise event-derived values may be reconstructed up to the requested timestamp.

For example, K/D/A may reflect champion-kill events occurring between the selected frame and `T`, while frame-backed gold remains the value observed at the selected frame.

The distinction between frame-observed state and event-derived state must remain explicit.

---

## Changes(T1, T2)

`Changes(T1, T2)` compares reconstructed state between two requested timestamps.

It should provide:

* numeric deltas for supported state values,
* relevant discrete events occurring in `(T1, T2]`,
* actual frame timestamps used for each requested state,
* provenance for derived values where useful.

The event interval is:

```text
(T1, T2]
```

Events exactly at `T1` belong to the prior state and are not included in the change interval.

Events exactly at `T2` are included.

No attempt should be made to infer continuous changes between observed frames.

---

## Initial Player State

The first reconstruction model should include only state currently required for useful temporal comparison.

Initial player state:

```text
ParticipantId

PositionX
PositionY

TotalGold
CurrentGold

Xp
Level

JungleMinionsKilled
LaneMinionsKilled

CurrentHealth
MaxHealth

CurrentResource
MaxResource

Kills
Deaths
Assists
```

The following distinction applies:

### Direct frame state

* position,
* total gold,
* current gold,
* XP,
* level,
* jungle CS,
* lane CS,
* health,
* resource state.

### Event-derived state

* kills,
* deaths,
* assists.

K/D/A is reconstructed deterministically by replaying champion-kill events up to the requested timestamp.

---

## Jungle CS Semantics

Riot's `jungleMinionsKilled` field will be represented as:

> **Jungle CS**

Pathwise must not describe this value as:

* camps cleared,
* number of camps,
* jungle clears,
* camp cycles.

The timeline does not reliably identify ordinary camp kills or camp respawn state.

Any future camp-level reconstruction would require a separate explicitly designed inference system.

---

## Opposing Jungler Identification

The opposing jungler is identified only when exactly one opposing participant has:

```text
teamPosition == "JUNGLE"
```

Pathwise will not infer the opposing jungler using:

* Smite,
* champion selection,
* jungle CS,
* itemization,
* pathing,
* or other heuristics.

If Riot's supplied role information is missing or ambiguous, the opposing jungler will be represented as unresolved.

Reconstruction must still remain usable for the configured player when the enemy jungler cannot be resolved.

---

## Event Model

The initial reconstruction event stream may include:

* champion kills,
* elite-monster kills,
* structure kills,
* item transactions,
* ward actions,
* level-up events.

Events should retain their original timestamp and useful source identifiers.

The reconstruction layer should not normalize every field available in Riot events.

Only fields currently needed for reconstruction or future explainability should be mapped.

---

## Objective Attribution

Riot timeline events can occasionally contain ambiguous or unusual attribution such as:

```text
killerId = 0
killerTeamId = 300
teamId = 0
```

Pathwise must not force such events onto a player or team merely to simplify downstream analysis.

Where attribution cannot be reliably determined, preserve an explicit:

* Unknown,
* Neutral,
* or equivalent unresolved state.

Source truth takes precedence over convenient interpretation.

---

## Position Semantics

Raw timeline coordinates will initially be retained as:

```text
PositionX
PositionY
```

Pathwise will not initially classify coordinates into semantic regions such as:

* top river,
* bot river,
* enemy jungle,
* own jungle,
* dragon pit,
* baron pit.

Map-region classification is deferred to a future analytical layer.

Because frame positions are approximately one minute apart, Pathwise must not present them as continuous movement paths.

---

## Inventory

Inventory-at-time reconstruction is deferred.

Although item transactions are available as precise events, Riot timelines contain purchase, sale, undo, destruction, transformation, consumable, trinket, and jungle-item behaviors that require careful replay semantics.

The initial reconstruction may expose item transaction events, but it must not claim an authoritative inventory state at arbitrary timestamps.

---

## Explicitly Deferred

The first reconstruction slice will not implement:

* inventory-at-time,
* ordinary camp identities,
* camp clear times,
* camp availability,
* camp respawn tracking,
* jungle-route inference,
* recall inference,
* exact base visits,
* map-region classification,
* ward coordinates,
* ability cooldown state,
* Smite charges,
* champion-specific skill/evolution analysis,
* damage analytics,
* continuous movement,
* metrics,
* signals,
* review-window detection,
* recommendations,
* AI interpretation.

These may be introduced later as separate analytical layers when supported by a concrete requirement.

---

## Persistence

Game reconstruction should be derived from retained raw Riot data.

The reconstruction model should not require normalization of the complete Riot timeline into relational database tables.

The preferred initial strategy is:

```text
Stored raw Match-V5 / Timeline-V5
        ↓
Infrastructure parsing
        ↓
Deterministic in-memory reconstruction
        ↓
Application query
```

Reconstruction persistence or caching should only be introduced if performance or another concrete requirement justifies it.

---

## Regression Fixture

Match:

```text
EUW1_7988789083
```

has been selected as the first candidate reconstruction regression fixture.

Relevant characteristics:

* Ranked Solo,
* Kha'Zix jungle,
* opposing Bel'Veth jungle,
* 28:11 duration,
* Kha'Zix final K/D/A: 16/6/3,
* Bel'Veth final K/D/A: 11/4/5,
* complete and valid Match-V5 payload,
* complete and valid Timeline-V5 payload,
* dense combat and objective activity,
* significant changes in gold, XP, level, jungle CS, and deaths over time,
* manually recognizable by the user.

Before committing the fixture, personal Riot identifiers should be sanitized without changing:

* participant IDs,
* team relationships,
* event timestamps,
* analytical values,
* participant linkage,
* event attribution.

The fixture should preserve enough raw source information to act as a deterministic regression input.

---

## Consequences

### Benefits

This approach:

* remains faithful to Riot source precision,
* avoids fabricated intermediate state,
* creates a stable primitive for later analytics,
* keeps reconstruction deterministic and testable,
* avoids premature database expansion,
* preserves the ability to evolve analytics from raw historical data,
* makes future observations traceable back to source evidence.

### Limitations

The model cannot initially answer with certainty:

* which ordinary camps were cleared,
* the exact jungle route,
* when a player recalled,
* exact movement between frames,
* exact state between frames for values Riot only snapshots,
* inventory at arbitrary timestamps,
* what information the player had through fog of war,
* player intent.

Future Pathwise features must respect these limitations rather than hiding them behind confident interpretation.

---

## Guiding Rule

When reconstructing a match:

> Preserve what Riot observed, derive only what can be derived deterministically, and leave the rest unknown.
