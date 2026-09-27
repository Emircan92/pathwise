# ADR 0007: Progression Evidence V1

## Status

Accepted.

## Context

Combat-focused review output omitted deterministic structure and match-ending facts already present in stored Match-V5 and Timeline-V5 payloads. That omission made real base progression look like an isolated sequence of fights to downstream consumers.

## Decision

Progression Evidence V1 is a sibling evidence layer alongside factual observations, game-knowledge annotations, and encounters. A pure Domain projector emits an ordered match chronology and `(start,end]` slices for selected review windows.

V1 includes Timeline-V5 `BUILDING_KILL`, Rift Herald `ELITE_MONSTER_KILL`, `ITEM_DESTROYED` for item `3513`, and `GAME_END`. Match-level outcome context additionally carries selected Match-V5 result, surrender, and participant Nexus fields with their JSON-field provenance.

The layer distinguishes:

- **Fact:** a source-reported timeline event or Match-V5 field.
- **Safe derivation:** deterministic selection, ordering, window inclusion, or a winner agreed by available outcome sources.
- **Interpretation:** tactical or strategic meaning assigned to those facts. Interpretation is excluded from V1.

For `BUILDING_KILL`, `teamId` is the destroyed structure's owning team. Nullable Riot fields remain nullable. Inhibitor and Nexus-turret events are destruction history only: V1 has no inhibitor respawn/current-state engine and invents no identity for the two Nexus turrets.

Timeline `GAME_END` supplies precise match-relative timing and winner attribution. It is not treated as proof of Nexus destruction. Match-V5 Nexus fields describe final participant results and have no exact destruction timestamp. Timeline and Match-V5 provenance remain distinct; conflicting winner claims are retained, the resolved winner is omitted, and a source-data issue is emitted.

A Rift Herald kill and a later participant-attributed item-3513 destruction are separate facts. V1 does not claim Herald possession duration, summon, deployment, charge, structure damage, or causation.

Assisting participant IDs remain source-reported contribution. They do not establish membership in the killer's team, objective benefit, cooperation, or shared ownership.

## Consequences

- Existing complete stored payloads gain progression evidence on demand without schema migration or Riot re-fetch.
- Encounter grouping and review-window selection remain unchanged.
- Future narrative interpretation can consume explicit macro facts without receiving raw Riot payloads or inheriting deterministic-layer intent claims.
