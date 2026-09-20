# ADR 0005: Game Knowledge Annotation Policy V1

## Status

Accepted.

This ADR establishes Pathwise Knowledge V1 as a deterministic enrichment layer that sits beside immutable factual observations.

The analysis pipeline is:

```text
reconstruction
→ review-window detection
→ factual observations
→ game-knowledge annotations
→ future recommendations
→ future AI explanation
```

Knowledge V1 does not modify earlier layers.

---

## Context

Pathwise currently provides:

* persisted Riot Match-V5 and Timeline-V5 data,
* deterministic reconstruction,
* ReviewWindowDetector V2,
* FactualObservationGenerator V1,
* MatchReview composition.

The factual-observation layer deliberately describes only reconstructed facts.

It does not know League mechanics such as objective spawn timings, and it does not interpret strategy.

The next layer should allow Pathwise to attach relevant League rules beside selected review windows and factual observations while preserving the factual analysis unchanged.

An initial design used exact full Riot build allowlists.

A source audit found that Riot strongly documents the relevant mechanics for public patch 26.18 but does not appear to publish an authoritative mapping from full Match-V5 build `16.18.817.5716` to public patch 26.18.

CommunityDragon and Riot Data Dragon provide sufficient technical evidence to associate the `16.18` version family with public patch `26.18`.

Requiring Riot-authenticated full-build mappings would add substantial complexity without meaningful product benefit.

Knowledge V1 therefore uses explicit public-patch compatibility rather than exact full-build allowlists.

---

## Decision

Add a deterministic game-knowledge annotation layer.

Knowledge V1 is:

* facts-only,
* public-patch aware,
* locally versioned,
* sourced,
* static,
* read-only at runtime.

It contains no strategic heuristics or recommendations.

Knowledge annotations may add contextual game mechanics beside existing selected windows and factual observations.

They must never modify:

* reconstructed state,
* review-window selection,
* factual-observation values,
* factual-observation eligibility,
* factual-observation ordering,
* provenance from earlier analysis stages.

---

## Knowledge Boundary

Knowledge V1 contains only verifiable game facts.

Examples include:

* objective initial-spawn timing,
* other explicitly documented mechanics added in future packs.

Knowledge V1 does not contain strategic heuristics such as:

* expected first clears,
* recommended resets,
* tempo principles,
* objective setup,
* farming-versus-fighting choices,
* matchup judgments,
* power-spike interpretations.

Those require a separately approved future knowledge version.

A commonly accepted strategic idea does not become a game fact merely because it is widely used.

---

## Public-Patch Compatibility

Knowledge compatibility is based on an explicit public League patch.

The original raw Riot `gameVersion` remains unchanged and preserved for provenance.

A deterministic patch resolver maps known Riot version families to public patches.

For V1:

```text
16.18 → 26.18
26.18 → 26.18
```

Therefore values such as:

```text
16.18.817.5716
16.18.8175716
16.18
```

resolve to:

```text
26.18
```

Numeric build suffixes do not affect compatibility once the version family is recognized.

No general arithmetic conversion such as:

```text
internal major + 10 = public major
```

is allowed.

Each version-family association must be added explicitly when understood.

Unknown or malformed version families remain unresolved.

---

## Patch-Pack Matching

A knowledge pack applies only when:

```text
resolved public patch == pack public patch
```

exactly.

Knowledge V1 does not support:

* nearest-patch fallback,
* previous-patch fallback,
* forward inheritance,
* backward inheritance,
* latest-pack fallback.

If a match resolves to another public patch and no exact pack exists, knowledge is withheld.

This does not make the factual review unavailable.

---

## Version-Mapping Evidence

Mechanic truth and version identification have separate evidence requirements.

### Mechanic facts

First-party Riot sources are the preferred authority for game mechanics.

### Version mapping

Reputable technical or community-maintained metadata may be used when Riot does not expose the required mapping directly.

For the V1 association:

```text
16.18 → 26.18
```

the accepted evidence includes:

* the Riot-derived Match-V5 raw version,
* CommunityDragon compatibility/release metadata,
* Riot Data Dragon version metadata,
* Riot public patch/release information,
* the completed Pathwise source audit.

The resolution basis must state that this association is derived from accepted technical metadata.

It must not imply Riot authenticated the full Match-V5 build mapping.

No per-build activation audit is required after the version family has been accepted.

---

## Patch Resolution

Domain exposes a patch-resolution result containing:

* raw Riot game version,
* optional resolved public patch,
* textual basis for the resolution.

Conceptually:

```csharp
public sealed record PublicPatch(int Major, int Minor);

public sealed record PatchResolution(
    string RawGameVersion,
    PublicPatch? Patch,
    string Basis);
```

Unknown identity is an expected runtime state, not an error.

---

## Knowledge Pack

Knowledge packs are small, immutable, checked-in values.

The initial pack is:

```text
ID:      summoners-rift-objective-initial-spawns
Version: 1
Patch:   26.18
Map:     11
Queue:   420
```

Knowledge data is owned by Application and passed into pure Domain logic.

V1 does not require:

* JSON/YAML loaders,
* database storage,
* runtime HTTP calls,
* provider abstractions,
* configurable rule languages,
* source catalogs,
* generic plugin architecture.

---

## Initial Knowledge Facts

Version 1 contains exactly two mechanics.

### Elemental Dragon Initial Spawn

```text
Fact ID:
elemental-dragon.initial-spawn

Initial spawn:
300,000 ms
5:00
```

This fact is supported by Riot mechanic documentation and the completed source review through public patch 26.18.

### Baron Nashor Initial Spawn

```text
Fact ID:
baron-nashor.initial-spawn

Initial spawn:
1,200,000 ms
20:00
```

This fact is supported by Riot's documented Baron timing changes and review through public patch 26.18.

The initial pack intentionally proves the knowledge pipeline with a tiny knowledge surface.

---

## Minimal Provenance

Each mechanic fact retains direct source references consisting of:

* title,
* URL.

The pack additionally retains a concise source-review note explaining that the mechanic values were reviewed through the pack's public patch.

The knowledge result preserves:

* raw Riot game version,
* resolved public patch,
* patch-resolution basis,
* knowledge-pack ID/version,
* mechanic sources,
* pack source-review note.

V1 does not require:

* source snapshots,
* hashes,
* source-authority scores,
* curator records,
* review timestamps,
* confidence percentages,
* exact-build verification records.

---

## Knowledge Coverage

Knowledge V1 reports one of four coverage states:

```text
Available
UnknownPatch
NoPackForPatch
UnsupportedMatch
```

### UnknownPatch

The match's public patch cannot be resolved.

### NoPackForPatch

The patch resolves successfully but no exact matching pack exists.

### UnsupportedMatch

A matching patch pack exists, but its map/queue scope does not cover the match.

### Available

A matching pack covers the match.

`Available` does not imply that every review window receives an annotation.

Zero annotations may be valid.

Unavailable knowledge never invalidates reconstruction, review-window detection, or factual observations.

---

## Annotation Types

Knowledge V1 supports two annotation kinds:

```text
NearInitialSpawn
RecordedObjectiveContext
```

### Near Initial Spawn

For each selected review window, consider the interval surrounding an objective's documented initial spawn:

```text
[spawn - 60 seconds, spawn + 60 seconds]
```

Emit a window annotation when the selected window's `(start, end]` interval intersects that range.

The exact intersection rule is:

```text
end >= lower && start < upper
```

The 60-second context radius is an editorial relevance rule.

It is not a League mechanic and must not be interpreted as an objective-setup recommendation.

---

### Recorded Objective Context

When an existing `EliteObjectiveContextObservation` contains a matching objective kill, attach the relevant initial-spawn fact directly to that observation/event.

Matching rules:

Baron:

```text
BARON_NASHOR
```

Elemental Dragon:

```text
DRAGON
```

with one of:

```text
AIR_DRAGON
EARTH_DRAGON
FIRE_DRAGON
WATER_DRAGON
HEXTECH_DRAGON
CHEMTECH_DRAGON
```

Do not treat:

* Elder Dragon,
* unknown Dragon subtypes,

as Elemental Dragon.

When a matching objective event exists, omit a redundant window-only annotation for the same fact.

---

## Kill Before Initial Spawn

If a recorded matching objective kill occurs earlier than the documented initial spawn:

* preserve the factual event unchanged,
* do not emit the knowledge annotation.

Knowledge V1 does not add a contradiction-reporting framework.

The knowledge layer must never modify historical evidence to match the knowledge pack.

---

## Annotation Meaning

An annotation states only that:

> this objective has this documented initial-spawn mechanic for the applicable patch.

For a recorded objective kill, it does not establish:

* that the objective's spawn was observed,
* that this was the first kill,
* how long the objective was alive,
* that it was continuously available,
* that it was accessible,
* that it was contestable,
* that either team should have taken or contested it.

---

## Knowledge Targets

Every annotation targets a selected review window.

An annotation may additionally reference:

* an existing factual observation,
* a specific source event.

An observation reference must belong to the target window.

An event reference must belong to the referenced observation.

Knowledge does not create replacement copies of factual observations.

---

## Identity and Ordering

Annotation identity is derived from:

* knowledge generator version,
* pack ID,
* pack version,
* fact ID,
* target,
* annotation kind.

A dedicated identity type is unnecessary in V1.

Annotations are deduplicated by their full identity.

Distinct source events remain distinct even when they share timestamps.

Annotations are ordered deterministically by:

1. selected-window bounds,
2. annotation kind,
3. fact ID using ordinal comparison,
4. event timestamp,
5. source frame index,
6. source event index.

Factual-observation ordering remains unchanged.

---

## Application Composition

Extend `MatchReview` with a knowledge result.

The runtime flow becomes:

```text
MatchReviewService
    → ReviewWindowService
        → one source load
        → reconstruction
        → ReviewWindowDetector V2
    → FactualObservationGenerator V1
    → resolve public patch
    → select exact public-patch knowledge pack
    → KnowledgeAnnotationGenerator V1
    → return MatchReview
```

The match must not be:

* loaded twice,
* reconstructed twice,
* redetected,
* re-observed.

Unknown knowledge coverage still returns a usable factual match review.

---

## Regression Fixture

For the existing Kha'Zix-versus-Bel'Veth fixture:

```text
Raw gameVersion:
16.18.817.5716

Resolved public patch:
26.18
```

The existing selected review window:

```text
23:41.654 – 27:00.500
```

continues to contain its unchanged five factual observations.

Two adjacent knowledge annotations are added.

### Dragon

Target:

```text
existing EliteObjectiveContext observation
source event (24,30)
```

Fact:

```text
elemental-dragon.initial-spawn
5:00
```

Recorded kill:

```text
23:47.715
```

Annotation kind:

```text
RecordedObjectiveContext
```

### Baron

Target:

```text
existing EliteObjectiveContext observation
source event (26,7)
```

Fact:

```text
baron-nashor.initial-spawn
20:00
```

Recorded kill:

```text
25:05.113
```

Annotation kind:

```text
RecordedObjectiveContext
```

No window-only duplicate annotations are created for those facts.

The gold, XP, Jungle CS, combat, and objective observations remain unchanged.

---

## Explicit Non-Inferences

Knowledge V1 does not infer:

* mistakes,
* correct decisions,
* best alternatives,
* causation,
* player intent,
* mechanical execution,
* pathing quality,
* recall quality,
* camp availability,
* lane priority,
* vision state,
* objective value,
* objective contest quality,
* whether an objective should have been contested,
* whether a player should have reset,
* champion strength,
* matchup state,
* power spikes,
* scaling,
* item effectiveness,
* strategic tempo.

Knowledge V1 is factual context only.

---

## Deferred Knowledge

The following are intentionally deferred:

* ordinary camp spawn/respawn systems,
* inferred camp availability,
* jungle route reconstruction,
* XP tables,
* champion-specific mechanics,
* item/rune mechanics,
* champion power spikes,
* matchup knowledge,
* first-clear heuristics,
* reset/tempo heuristics,
* strategic objective heuristics.

Future additions require deliberate scope review.

---

## Consequences

### Benefits

Knowledge V1:

* proves the knowledge-enrichment pipeline,
* keeps factual observations immutable,
* is patch-aware,
* remains inspectable and deterministic,
* tolerates unavailable knowledge,
* avoids runtime network dependencies,
* avoids per-build provenance overengineering,
* provides a stable seam for future knowledge.

### Limitations

Knowledge V1:

* contains only two mechanics,
* supports only patch 26.18,
* supports only Summoner's Rift queue 420 in its initial pack,
* does not understand objective alive-state,
* does not understand strategy,
* does not provide recommendations.

These are intentional V1 limits.

---

## Guiding Rule

> Add League knowledge beside the evidence without changing what the evidence says.
