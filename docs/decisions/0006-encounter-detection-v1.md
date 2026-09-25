# ADR 0006: Encounter Detection V1

## Status

Accepted. Candidate B from the revised grouping policy supersedes the original sequential grouping proposal.

## Decision

An **Encounter** is a group of recorded champion-kill events connected by timing, recorded location, and participant evidence. The UI calls these **Combat encounters**. Detection runs independently within each final selected review window. Every reconstructed champion kill in `(requestedStartTimestampMs, requestedEndTimestampMs]` belongs to exactly one encounter. Source `(frameIndex, eventIndex)` deduplication precedes canonical sorting by timestamp, frame index, and event index. A singleton is valid.

An event's participant set contains its known reconstructed killer, victim, and assisting participants, deduplicated by ID. Roles have equal weight. Unknown or non-player killer IDs do not contribute to overlap; original source fields remain unchanged.

### Fixed links and bounds

All comparisons are inclusive. Positioned pairs within 5,000 ms and 2,500 raw source-coordinate units have a **Local** link without requiring participant overlap. Positioned pairs within 20,000 ms and 2,500 units have an **Extended** link when at least one participant overlaps. A pair satisfying both is Local. If either position is absent, a **Missing-position fallback** link requires at most 10,000 ms and at least two distinct overlapping participants. Squared Euclidean distance comparisons use overflow-safe arithmetic.

Every resulting encounter has a first-to-last recorded kill span of at most 60,000 ms. Every pair of positioned member events is at most 5,000 source-coordinate units apart. These are whole-component bounds.

Construct all qualifying pairwise edges with canonically ordered endpoints. Sort by Local, Extended, then Missing-position fallback; within a tier by smaller time difference, smaller squared distance (unavailable last), larger participant overlap, earlier endpoint source ordering, then later endpoint source ordering. Start with one component per event. Process positioned edges first, merging components only if the entire union satisfies both bounds, and record each accepted source-event edge as grouping provenance. After this stage, identities of components containing positioned events are fixed. Process fallback edges with the same bounds; unpositioned events or groups may attach, and entirely unpositioned groups may form, but fallback evidence must never merge two distinct positioned components. Return members in canonical order and encounters by first timestamp, last timestamp, then first source reference. Input enumeration order cannot affect the result. Unrestricted connected-components clustering is not used.

### Context and summary

Elite-monster kills never seed, extend, or merge encounters or add participants. An existing objective event in the same window associates non-exclusively with an encounter when it and at least one positioned combat member are within 30,000 ms and 2,500 units. Both positions are required. Preserve objective source identity and attribution. Association is context, not evidence that the fight was for the objective, the objective was contestable, or the fight caused its kill.

Each encounter reports its stable ID, first and last recorded kill timestamps, their difference, combat event count, sorted distinct participant IDs and count, configured-player involvement/kills/deaths/assists/distinct involved-event count, nullable enemy-jungler involvement, complete combat events, associated objective events, and accepted grouping edges. Missing or ambiguous enemy resolution yields null involvement.

The ID is `enc-v1-` plus lowercase SHA-256 of one compact UTF-8 JSON positional array: match ID, configured participant ID, resolved enemy participant ID or null, requested window start and end, reconstruction version, review-detector version, encounter-detector version, and sorted `[[frameIndex,eventIndex], ...]` combat membership. One canonical serializer owns this format. Rank, input order, UI labels, list position, and objective associations cannot affect identity. Encounter detector version is 1; changes to grouping semantics require a version increment.

### Ownership

The pure Domain detector accepts `GameReconstruction` and `ReviewWindowDetectionResult` and owns policy, grouping, summary, objective association, provenance, and identity. `MatchReviewService` composes it as a sibling to factual observations and knowledge annotations using the already-loaded reconstruction and final selected windows. The result is keyed by the existing `SelectedReviewWindowKey`. The review API adds `versions.encounters` and `windows[].encounters` through explicit DTO mapping. The frontend owns selection and map/evidence filtering. No persistence, source load, Riot call, runtime dependency, or change to earlier analysis semantics is introduced.

### Calibration and limitations

Read-only planning simulations over five stored Kha'Zix games yielded 77 encounters, including 44 singletons, under this policy. The committed fixture is expected to yield 3, 6, 2, 4, and 4 encounters across its selected windows. In the 23:41.654–27:00.500 window, the 24:41.654, 24:45.227, 24:48.530, and 24:49.049 kills group together. Later connecting evidence can reconcile earlier groups while the component bounds prevent long spatial or temporal chains. The sample is combat-heavy, has positioned kill events, and does not establish universal fight boundaries. Short nearby exchanges can falsely merge; real pursuits, kill-free lulls, and window boundaries can fragment. Singletons remain visible.

Encounter V1 does not infer full fight duration, continuous presence, movement, paths, participant intent, fight quality, winners or losers, objective causation or contestability, tactical mistakes, or recommendations. Frame samples shown nearby in the UI are context, not proof of event participation or exact presence.
