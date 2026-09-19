# Kha'Zix vs Bel'Veth loss 001

Frozen, sanitized Match-V5 and Timeline-V5 payloads for deterministic reconstruction regression tests.

- Synthetic match ID: `EUW1_1`
- Synthetic numeric game ID: `1`
- Synthetic game start: `2026-09-01T00:00:00Z`
- Queue/map: Ranked Solo (`420`), Summoner's Rift (`11`)
- Patch: `16.18.817.5716`
- Configured participant: `2` (Kha'Zix, team 100)
- Opposing jungler: `7` (Bel'Veth, team 200)

Sanitization replaces participant PUUIDs and player/account identifiers with participant-indexed synthetic values, replaces match/game identifiers, and applies one common offset to absolute epoch timestamps. Relative frame/event timing, scheduled objective time, duration, intervals, participant IDs, team relationships, gameplay values, event order, and attribution anomalies are preserved. No reversible identity mapping is retained.

Reviewed checkpoints include frames at `300065`, `600219`, `900315`, `1200393`, and `1500470`; the unknown soul marker at `759248`; the team-200 soul marker at `1428216`; Baron at `1505113`; and final coverage at `1691676`.
