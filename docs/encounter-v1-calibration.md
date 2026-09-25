# Encounter Detection V1 calibration

The fixed Candidate B policy is recorded in [ADR 0006](decisions/0006-encounter-detection-v1.md). These counts are deterministic outputs from the implemented review endpoint over five locally stored Kha'Zix games on 2026-09-25. The request read existing match and timeline payloads; it did not fetch Riot data.

| Match | Selected windows | Encounters | Singletons |
|---|---:|---:|---:|
| EUW1_7989757419 | 4 | 12 | 8 |
| EUW1_7989680408 | 5 | 23 | 13 |
| EUW1_7988870205 | 2 | 10 | 6 |
| EUW1_7988789083 | 5 | 19 | 9 |
| EUW1_7988661903 | 3 | 13 | 8 |
| **Total** | **19** | **77** | **44** |

The committed sanitized fixture is covered by automated regression tests. Its selected windows yield **3, 6, 2, 4, 4** encounters. In the 23:41.654–27:00.500 window, the 24:41.654, 24:45.227, 24:48.530, and 24:49.049 kills form one encounter from source references `(25,22)`, `(25,23)`, `(25,24)`, `(25,26)`. The Baron kill associates with that encounter; the distant Dragon kill does not associate with the 23:47.147 singleton. These results reproduce the approved planning calibration. They are evidence-grouping checks, not replay-verified fight boundaries.
