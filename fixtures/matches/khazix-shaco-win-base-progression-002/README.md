# Kha'Zix vs Shaco win: base progression 002

Frozen, sanitized Match-V5 and Timeline-V5 payloads for Structure & Match Progression Evidence V1 regression tests.

- Synthetic match ID: `EUW1_2`
- Synthetic numeric game ID: `2`
- Synthetic game start: `2026-09-02T00:00:00Z`
- Queue/map: Ranked Solo (`420`), Summoner's Rift (`11`)
- Patch: `16.18.817.5716`
- Configured participant: `2` (Kha'Zix, team 100)
- Opposing jungler: `7` (Shaco, team 200)

This fixture protects the real calibration case where combat evidence was correct but omitted meaning-changing deterministic macro facts. Its late review sequence contains team-200 mid and top base-turret/inhibitor destruction, both team-200 Nexus-turret destruction events, two separate combat encounters, and `GAME_END` with team 100 reported as winner.

It also preserves Kha'Zix's earlier Rift Herald kill and later item-3513 destruction as separate facts without asserting summon, deployment, charge, or structure causation. At the Hextech Dragon kill, Shaco/team 200 remains the killer/team attribution while Kha'Zix/team 100 remains among Riot's source-reported assists; that assist does not imply team membership, cooperation, or benefit.

Sanitization replaces participant and account identities, assigns synthetic match/game identity, and applies one common offset to absolute epoch timestamps. Relative frame/event timing, duration, participant IDs, team relationships, gameplay values, event order, and attribution anomalies are preserved. No reversible identity mapping is retained.
