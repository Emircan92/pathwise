# Real-match calibration fixtures

This directory is a small, purposeful calibration corpus of frozen, sanitized Riot Match-V5 and Timeline-V5 payloads.

Each fixture must protect a materially distinct deterministic semantic pattern or an important real-world calibration failure. Fixtures are not collected to reach a target count, and random or bulk match collection is out of scope. Isolated rules belong in handcrafted tests; full fixtures protect end-to-end behavior that depends on realistic Riot payload combinations.

Every fixture README records its synthetic identity and regression purpose. Sanitization must remove original player and match identities without retaining a reversible mapping, while preserving relative timing, gameplay values, attribution, and event ordering.
