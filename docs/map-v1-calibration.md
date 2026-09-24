# Map V1 asset and projection calibration

## Pinned artwork

- Source: https://ddragon.leagueoflegends.com/cdn/16.18.1/img/map/map11.png
- Riot Data Dragon version: `16.18.1`; map ID: `11` (Summoner's Rift)
- Retrieved: 2026-09-24; unmodified PNG, 512 × 512, 70,985 bytes
- SHA-256: `5b446777c3e8491c1ab1860bc8fd448ad58f46cf3630d909f74dd3dc3dda8cd1`
- Local file: `src/Pathwise.Web/public/maps/riot/16.18.1/map11.png`
- Calibration identifier: `map11-16.18.1-fixture-001`

This is Riot-owned static reference art. It is not covered by any claim that the project's code license applies to all assets. See [Riot's Data Dragon documentation](https://developer.riotgames.com/docs/lol#data-dragon_other), [developer policy and terms](https://developer.riotgames.com/terms), and [legal notice](https://www.riotgames.com/en/legal). The image is schematic and has no explicit turret glyphs. The fixture reports game version `16.18.817.5716`; version similarity alone does not establish terrain equivalence.

## Measurement and fit

Image pixels are measured from the upper-left of the full, uncropped 512 × 512 PNG. Two visibly identifiable pit circles provided point landmarks. Their centers were read to about ±3 px. Event coordinates represent recorded kill locations, not authoritative pit centers, so fitting to these points has that additional uncertainty.

| Fit landmark | Fixture source | World (x, y) | Image center (x, y) | Residual |
| --- | --- | ---: | ---: | ---: |
| Baron pit | frame 26/event 7, Baron at 1505113 ms | (5007, 10471) | (170, 152) | 0 px by fit |
| Dragon pit | frame 7/event 48, Dragon at 419447 ms | (9859, 4435) | (345, 360) | 0 px by fit |

The axis-aligned fit, with no individual-point adjustment, is:

```text
imageX = 0.03606760098928277 × sourceX - 10.590478153338836
imageY = -0.03445990722332671 × sourceY + 512.8296885354539
```

It has the required positive X and negative Y scale. The full image rectangle is used at every rendered size; no team rotation or fixture-minimum/maximum normalization is applied.

## Independent checks and gate

The fitting events above were withheld from the following checks. The pit art supports approximate regions, not exact kill points. Measured image regions are Dragon `(330–365, 335–385)` and upper pit `(145–190, 130–175)`; these are local river features, not broad halves of the map.

| Held-out source | Projected pixel | Check |
| --- | ---: | --- |
| Dragon, frame 13/event 22, (10020, 5038) | (350.8, 339.2) | Dragon region; 21.6 px from art center. This kill is not treated as a point-center anchor. |
| Dragon, frame 19/event 8, (9926, 4522) | (347.4, 357.0) | Dragon region; 3.9 px from center. |
| Dragon, frame 24/event 30, (9837, 4397) | (344.2, 361.3) | Dragon region; 1.5 px from center. |
| Three Horde events, frame 9 | (162.2, 162.0), (177.3, 153.6), (164.0, 146.2) | Upper pit region; 7.5–12.7 px from center. |
| Rift Herald, frame 18/event 15 | (154.8, 155.2) | Upper pit region; 15.5 px from center. |

All 18 available structure positions were also inspected on the unmodified image. The two outer top turrets project to `(24.8, 153.0)` and `(145.1, 34.7)` on the top corridor; bottom outer turrets to `(368.3, 477.4)` and `(489.5, 357.6)` on the bottom/right corridor; mid outer turrets to `(200.3, 292.4)` and `(312.4, 219.6)` along the center diagonal. Blue inhibitors and nexus turrets project into the lower-left base graphic. No structure is used as an exact pixel-center anchor because no turret icon exists on this art. Frames 23–27 were overlaid for both junglers without moving any points; they follow the expected base, lane, jungle, and river orientation. The frame-24 configured-player sample near `(6, 489)` lies at the schematic's dark lower-left edge, consistent with base/fountain uncertainty rather than a precise terrain landmark.

**Gate result: pass for approximate spatial context.** Held-out objectives remain within the identifiable local pit regions, the structure layout matches lane and base corridors, and no point needs manual distortion or crosses the wrong lane/river boundary. Only held-out Dragon events close to their center satisfy a 2%-width point residual; the other event coordinates are evaluated as positions within a pit region because their kill locations are not point-center anchors. The transform is empirical and should not be described as authoritative world bounds or exact terrain reconstruction.
