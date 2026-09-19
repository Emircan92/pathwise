# LoL Jungle Review — MVP Specification

## 1. Product Summary

LoL Jungle Review is a locally hosted personal League of Legends analytics application focused on helping a jungler answer one primary question after a ranked game:

> **What should I have done differently?**

The application retrieves match and timeline data from the Riot API, reconstructs the important progression of a game, identifies the periods most deserving of review, and presents those periods in concise, human-readable language.

Statistics, graphs, timelines, and comparisons exist primarily as evidence supporting the review rather than as the product's main output.

The MVP is designed for personal use and will initially analyze jungle games only.

---

## 2. Product Goal

The MVP should allow the user to open a recently played ranked jungle game and quickly understand:

* what the important phases of the game were,
* where the user's game state materially improved or deteriorated,
* which 2–4 periods deserve the most attention,
* what measurable changes occurred during those periods,
* and what alternative action may have produced a better outcome.

The application should prioritize explanation over raw statistics.

A successful review should reduce a confusing match from:

> “I was ahead, got a lot of kills, and somehow we still lost.”

into something closer to:

> “You established an early lead, but between minutes 12–18 your jungle income fell substantially while you remained involved in repeated fights. Your gold advantage contracted during this period, and the enemy secured the next neutral objective. The strongest alternative opportunity was to reset and clear your spawning bottom-side camps after the fight at 13:40 before playing toward dragon.”

The system should make clear which parts are factual observations and which parts are inferred recommendations.

---

## 3. MVP Scope

### Supported

The MVP will support:

* one local user,
* ranked League of Legends matches,
* jungle games only,
* manual retrieval of recent matches,
* local persistence,
* individual match review,
* chronological reconstruction of important game periods,
* comparison against the opposing jungler where useful,
* limited comparison against the user's own historical games where enough data exists,
* human-readable observations and review recommendations,
* supporting graphs, statistics, and event timelines.

### Champion Scope

The architecture must be champion-agnostic.

No core data model, analytics pipeline, or review system should assume that the player is using Kha'Zix.

However, development and validation will initially use Kha'Zix games heavily because they provide a familiar and consistent dataset.

Champion-specific analysis may be introduced later as an additional interpretation layer.

---

## 4. Explicit Non-Goals

The MVP will not attempt to:

* support all five roles,
* provide live-game assistance,
* provide opponent scouting,
* reproduce OP.GG, LeagueOfGraphs, or similar general-purpose stat sites,
* generate an alternative MMR or ranking system,
* provide detailed champion coaching for every champion,
* benchmark the user against a complete Diamond-player dataset,
* automatically determine the objectively correct play in every situation,
* analyze mechanical execution from replay video,
* provide real-time positional recommendations,
* support multiple users,
* require cloud hosting,
* provide native Windows packaging,
* expose public profiles,
* provide polished long-term progression dashboards,
* or depend on an LLM for core functionality.

These may be considered in later versions where appropriate.

---

## 5. Primary User Flow

The expected MVP workflow is:

1. User opens the local application.
2. User presses **Fetch Latest Matches**.
3. The application retrieves recent Riot match data.
4. Match and timeline responses are persisted locally.
5. Ranked jungle matches are identified.
6. The recent match list is displayed.
7. User selects a game.
8. The application reconstructs the game.
9. The application identifies approximately 2–4 review-worthy periods.
10. The user first sees a concise overall game review.
11. The user can inspect each review period chronologically.
12. Supporting statistics, events, graphs, and raw evidence are available beneath the explanation.

The review should be useful without requiring the user to interpret graphs manually.

---

## 6. Main Screens

### 6.1 Recent Matches

The landing screen should provide:

* Riot identity,
* current known rank where available,
* **Fetch Latest Matches** action,
* recently stored matches,
* champion,
* result,
* game duration,
* role,
* timestamp,
* analysis status.

Example:

```text
Recent Matches

Kha'Zix    LOSS    31:42    Jungle    Review
Kha'Zix    WIN     27:14    Jungle    Review
Wukong     WIN     34:51    Jungle    Review
Ahri       LOSS    29:08    Mid       Unsupported
```

Non-jungle games may be stored but do not require analysis.

---

### 6.2 Match Review

The match review screen is the centerpiece of the MVP.

The first visible section should answer:

> What should I take away from this game?

Example:

```text
KHA'ZIX — LOSS — 31:42

GAME REVIEW

You established an early economic lead but failed to
preserve it during the middle game.

The most important period was 13:10–17:45, when repeated
champion interactions coincided with lower jungle income,
a shrinking gold advantage, and the loss of the next
neutral objective.

3 periods deserve review.
```

The user should not need to open statistics before receiving this summary.

---

### 6.3 Review Period

Each review period should contain four sections.

#### Entering State

Describe the meaningful state when the period begins.

Examples:

* gold differential,
* level differential,
* jungle CS differential,
* recent objective state,
* important item timings,
* relevant map state where reliably available.

#### What Happened

Chronologically summarize meaningful events during the period.

Examples:

* kills,
* deaths,
* assists,
* camp activity,
* objective activity,
* recalls,
* item purchases,
* movements,
* major changes in economy or experience.

#### Resulting State

Explain how the user's position changed by the end of the window.

Example:

```text
Start:
+1,040 relative gold
+1 level

End:
+210 relative gold
levels equal

During this period:
You gained 22 jungle CS.
Enemy jungle gained 41.
You participated in four champion kills.
Enemy team secured dragon.
```

#### What Could Have Been Done Differently

Provide one or more plausible alternative actions where sufficient evidence exists.

Recommendations must be grounded in available match data.

The system should avoid presenting uncertain tactical interpretations as objective facts.

---

## 7. Analytical Layers

The application should maintain a distinction between three levels of output.

### Layer 1 — Facts

Directly measurable or reconstructable information.

Examples:

* player had 4,200 gold at minute 10,
* enemy jungler had 4,650,
* dragon was killed at 12:43,
* user died at 14:28,
* user's jungle CS increased by 18 during a window.

These should be deterministic.

### Layer 2 — Derived Observations

Rules or calculations based on factual data.

Examples:

* relative gold lead contracted,
* farming rate fell,
* champion interaction frequency increased,
* user spent an extended period without meaningful economy gain,
* objective control worsened,
* a death occurred while holding a significant lead.

These should remain deterministic wherever practical.

### Layer 3 — Recommendations

Interpretations answering:

> What could I have done differently?

Examples:

* consider resetting after a successful fight rather than immediately moving to another low-certainty interaction,
* clear available camps before contesting the next objective,
* avoid extending an invade after the initial advantage was secured.

Recommendations may involve uncertainty and should communicate that appropriately.

---

## 8. Initial Analytical Concepts

The following concepts should guide development, but their exact formulas do not need to be finalized before implementation.

Potential analytical signals include:

* gold advantage and contraction,
* XP advantage and contraction,
* jungle CS rate,
* relative jungle CS,
* time between meaningful economy gains,
* kill participation,
* deaths while ahead,
* repeated champion interaction,
* objective participation,
* objective conversion,
* activity before objective spawn,
* post-fight behavior,
* reset opportunities,
* item timing,
* periods of high activity with low economic gain,
* farm abandonment,
* lead preservation,
* lead conversion.

The MVP should begin with a small number of trustworthy signals rather than a large taxonomy of speculative mistakes.

---

## 9. Review Window Detection

The system should identify approximately 2–4 periods per game that are unusually meaningful.

A review window could be triggered by events such as:

* large gold swing,
* large XP swing,
* transition from ahead to even or behind,
* multiple deaths within a short period,
* meaningful lead contraction,
* objective loss,
* major objective fight,
* extended reduction in farming,
* unusually high champion interaction,
* missed economic opportunity,
* or another material change in game state.

The exact algorithm should evolve using real match data.

Initial implementation should favor explainability over sophistication.

Every selected review window should have a traceable reason for being selected.

---

## 10. Comparison Strategy

The opposing jungler is an important contextual comparison but is not the user's ultimate target.

The application should eventually support three perspectives:

1. **User vs opposing jungler**
2. **User vs historical self**
3. **User vs target performance benchmark**

For the MVP:

* opposing-jungler comparison is supported,
* historical-self comparison may be used once sufficient stored data exists,
* external rank benchmarking is deferred.

A future version may build benchmark datasets for ranks such as Diamond.

---

## 11. Historical Data

Historical analysis is not a primary MVP screen.

However, the system should be designed so every retrieved match contributes to a reusable personal dataset.

This enables future analysis such as:

* performance over the last 20/50/100 games,
* champion-specific patterns,
* improvement tracking,
* recurring loss patterns,
* averages and distributions,
* comparisons between successful and unsuccessful games.

Historical data may also provide context for current match analysis when appropriate.

Example:

> Your jungle CS at 15 was substantially below your median across the previous 30 comparable games.

---

## 12. Raw Data Retention

Raw Riot API responses should be retained locally.

At minimum, this includes:

* match data,
* timeline data,
* relevant player/account metadata.

Raw responses should not be discarded after transformation.

The goal is to allow new metrics and analytical models to be applied retroactively without retrieving the same Riot data again.

Derived data must be reproducible from stored source data where practical.

---

## 13. AI Integration

An LLM is intentionally not required for the core MVP.

The architecture should nevertheless make future AI integration straightforward.

Expected future pipeline:

```text
Riot API
    ↓
Raw persisted data
    ↓
Deterministic reconstruction
    ↓
Metrics
    ↓
Review windows
    ↓
Structured observations
    ↓
AI interpretation
```

The future AI layer should consume structured analytical output rather than raw Riot responses wherever practical.

This allows deterministic facts and metrics to remain inspectable and testable.

AI may later improve:

* natural-language explanations,
* tactical interpretation,
* recommendation quality,
* comparison of user recollection with match facts,
* prioritization of observations,
* longitudinal coaching.

---

## 14. User Recollection

A future iteration should allow the user to attach a short recollection to a match.

Example:

> “I felt like I completely dominated early, but Kayn somehow stayed ahead and eventually outscaled me.”

The system could compare that recollection against reconstructed facts.

This feature is desirable but not required for initial MVP completion.

The data model should avoid making its future addition unnecessarily difficult.

---

## 15. User Experience Principles

The interface should be pleasant and polished enough for regular personal use.

Pixel-perfect design is not required.

The application should follow these principles:

### Explanation first

The first thing presented should be meaning, not statistics.

### Evidence available

Every important conclusion should allow the user to inspect its supporting data.

### Progressive detail

The default view should remain concise.

Users should be able to drill into timelines, graphs, events, and metrics when desired.

### Chronological understanding

The application should help the user understand how the game changed over time.

### Avoid fake certainty

Where the data cannot determine the objectively correct decision, the UI should communicate alternatives and uncertainty rather than fabricate certainty.

---

## 16. Local-First Product

The MVP will run locally.

Expected characteristics:

* localhost web application,
* local database,
* locally configured Riot API credentials,
* no multi-user authentication requirement,
* no cloud infrastructure requirement,
* no deployment pipeline requirement for initial use.

The system should not embed API credentials in client-side code.

---

## 17. Engineering Quality

The MVP is a real software project rather than a disposable prototype.

Normal engineering standards should therefore apply without unnecessary enterprise complexity.

Expected standards include:

* clear project structure,
* separation of concerns,
* dependency injection where appropriate,
* database migrations,
* structured logging,
* configuration management,
* secret handling,
* unit tests for meaningful business logic,
* integration tests for important boundaries where practical,
* linting/formatting,
* understandable error handling,
* maintainable naming,
* documented architectural decisions where consequential.

Infrastructure should remain proportional to a single-user local application.

---

## 18. Development Model

AI coding tools such as Codex or Cursor will perform much of the implementation.

The human developer acts primarily as:

* product owner,
* technical lead,
* architecture reviewer,
* code reviewer,
* acceptance tester.

Early in development, implementation details will also be reviewed closely to establish trust and familiarity with the codebase.

As the project stabilizes, review may shift toward architecture, behavior, tests, and meaningful diffs rather than every low-level implementation decision.

AI developers may make small reversible implementation decisions autonomously.

They should stop and request approval for decisions that materially affect:

* architecture,
* persistence strategy,
* public contracts,
* significant dependencies,
* destructive migrations,
* security,
* major scope changes,
* or decisions expensive to reverse.

When uncertain whether a change is consequential, asking is preferred.

---

## 19. MVP Success Criteria

The MVP can be considered successful when the following workflow works reliably:

1. Configure Riot identity and API credentials.
2. Fetch recent matches.
3. Persist raw match and timeline data locally.
4. Identify ranked jungle games.
5. Open an analyzed jungle match.
6. Reconstruct meaningful game progression.
7. Identify approximately 2–4 review-worthy periods.
8. Present a concise overall game explanation.
9. Present factual evidence for each review period.
10. Provide at least one useful, evidence-supported alternative action for meaningful periods where the data permits one.
11. Allow the user to inspect supporting statistics and chronology.
12. Re-run analysis on already stored games without retrieving raw data again.

The MVP does **not** require the application to understand every League decision correctly.

It should instead demonstrate that reconstructed Riot data can repeatedly produce useful explanations of real jungle games.

---

## 20. Product Litmus Test

Every significant feature should be challenged with:

> **Does this help explain what happened in the game and what I could have done differently?**

If the answer is no, the feature is probably not required yet.

The product succeeds when it transforms a confusing game into a small number of understandable decisions or periods worth learning from.

The statistics prove the explanation.

They are not the explanation.
