# Pathwise — Architecture

## 1. Purpose

This document describes the initial technical architecture for **Pathwise**, a local-first League of Legends jungle review application.

Pathwise retrieves Riot match data, reconstructs jungle games, analyzes meaningful periods within those games, and presents evidence-backed explanations of what the player could have done differently.

The architecture should support the MVP while remaining flexible enough for later additions such as:

* richer historical analysis,
* rank-based benchmarking,
* champion-aware analytics,
* AI-generated interpretation,
* progress tracking,
* additional roles,
* and optional hosted deployment.

The project should remain intentionally simple where possible.

The goal is not enterprise architecture.

The goal is a maintainable architecture with strong boundaries around the parts of the system most likely to evolve.

---

# 2. Architectural Principles

## 2.1 Explanation is the product

Statistics, charts, Riot data, and metrics exist to support explanations.

The architecture should therefore center around producing a structured game analysis rather than merely exposing raw statistics.

---

## 2.2 Raw data must remain recoverable

Raw Riot match and timeline responses are retained locally.

Derived analytics should be reproducible from persisted source data whenever practical.

This allows analytics algorithms to evolve without repeatedly querying Riot.

---

## 2.3 Analysis must not depend on presentation

The analysis engine should know nothing about React, Next.js, HTTP presentation, charts, or UI formatting.

It should produce structured analysis results that could be consumed by:

* the web application,
* tests,
* command-line tooling,
* future AI integrations,
* or other clients.

---

## 2.4 Riot contracts must not become domain contracts

Riot DTOs belong at the integration boundary.

Pathwise domain and application logic should operate on Pathwise concepts rather than spreading Riot response models throughout the application.

---

## 2.5 Deterministic analysis before AI interpretation

Core reconstruction, metrics, signals, and review-window detection should remain deterministic wherever practical.

Future LLM functionality should consume structured Pathwise analysis rather than being responsible for interpreting raw Riot JSON.

---

## 2.6 Time is a first-class concept

League of Legends is fundamentally temporal.

Pathwise must be capable of reasoning about questions such as:

* What was the player's state at 10:00?
* What changed between 12:30 and 16:00?
* What happened immediately after a successful fight?
* What occurred before an objective spawned?
* When did an economic lead begin contracting?

The architecture should therefore favor time-indexed game reconstruction rather than analysis based solely on final statistics.

---

## 2.7 Prefer reversible decisions

During MVP development, prefer architectural and implementation decisions that are inexpensive to change.

Avoid introducing infrastructure solely because it may theoretically become useful later.

---

# 3. Technology Stack

## Frontend

* Next.js
* TypeScript
* App Router
* Tailwind CSS
* shadcn/ui
* charting library to be selected when chart requirements are clearer

The frontend is responsible primarily for presentation and interaction.

It should not contain meaningful game-analysis logic.

---

## Backend

* .NET 10
* ASP.NET Core
* C#
* conventional REST API
* built-in dependency injection
* standard .NET configuration and logging abstractions

---

## Persistence

* Entity Framework Core
* SQLite
* EF Core migrations

SQLite is sufficient for the local, single-user MVP while still allowing normal relational modeling and migration practices.

The persistence architecture should avoid leaking SQLite-specific behavior into domain logic.

---

## Repository

GitHub-hosted monorepo.

The repository contains:

* frontend,
* backend,
* tests,
* documentation,
* fixtures,
* scripts.

---

## Runtime model

Pathwise is initially a local application.

Typical development runtime:

```text
Next.js development server
        │
        │ HTTP
        ▼
ASP.NET Core API
        │
        ├── SQLite
        │
        └── Riot API
```

Docker is not required for MVP development.

Windows is the primary development environment.

The application itself should avoid unnecessary Windows-specific dependencies.

---

# 4. Repository Structure

Initial structure:

```text
pathwise/
│
├── src/
│   ├── Pathwise.Api/
│   ├── Pathwise.Application/
│   ├── Pathwise.Domain/
│   ├── Pathwise.Infrastructure/
│   └── Pathwise.Web/
│
├── tests/
│   ├── Pathwise.Domain.Tests/
│   ├── Pathwise.Application.Tests/
│   ├── Pathwise.Infrastructure.Tests/
│   └── Pathwise.Api.Tests/
│
├── fixtures/
│
├── docs/
│   ├── MVP.md
│   ├── ARCHITECTURE.md
│   └── decisions/
│
├── scripts/
│
├── .gitignore
└── README.md
```

Not every project must contain significant code immediately.

Projects should earn complexity through actual requirements.

---

# 5. Backend Project Responsibilities

## 5.1 Pathwise.Domain

Contains concepts belonging to Pathwise itself.

Examples may include:

```text
Match
Participant
Player
Champion
GameTime
GameSnapshot
JungleSnapshot
ObjectiveEvent
CombatEvent
EconomyState
Metric
Signal
ReviewWindow
Observation
Recommendation
GameAnalysis
```

Exact domain types should emerge from implementation rather than being fully designed upfront.

### Domain must not depend on

* Entity Framework Core,
* Riot API DTOs,
* HTTP,
* ASP.NET Core,
* Next.js,
* SQLite,
* external AI providers.

Domain logic should be independently testable.

---

# 5.2 Pathwise.Application

Contains application workflows and analytical orchestration.

Responsibilities include:

* importing fetched matches,
* determining whether a match is analyzable,
* reconstructing game state,
* calculating metrics,
* detecting signals,
* detecting review windows,
* producing observations,
* producing recommendations,
* rerunning analysis,
* retrieving stored analyses.

Potential use cases include:

```text
FetchRecentMatches
ImportMatch
AnalyzeMatch
ReanalyzeMatch
GetRecentMatches
GetMatchReview
```

The Application layer coordinates work.

It should not contain Riot-specific HTTP implementation or EF Core persistence details.

---

# 5.3 Pathwise.Infrastructure

Contains integrations and technical implementation details.

Responsibilities include:

* Riot API client,
* Riot DTO definitions,
* Riot response mapping,
* EF Core DbContext,
* SQLite persistence,
* repositories where useful,
* configuration,
* external service implementations,
* raw JSON persistence.

This is the only layer that should understand Riot's HTTP contracts directly.

---

# 5.4 Pathwise.Api

Contains the ASP.NET Core HTTP boundary.

Responsibilities include:

* REST endpoints,
* request validation,
* response mapping,
* dependency configuration,
* authentication/configuration handling where required,
* HTTP-level error handling.

Controllers or endpoint handlers should remain thin.

They should invoke Application workflows rather than contain analytical logic.

---

# 5.5 Pathwise.Web

Next.js application.

Responsibilities include:

* navigation,
* match listing,
* match review presentation,
* review-window visualization,
* charts,
* loading/error states,
* initiating match retrieval,
* requesting reanalysis,
* progressive disclosure of supporting evidence.

It consumes the Pathwise REST API.

It should not communicate directly with Riot.

---

# 6. Frontend Structure

Initial conceptual structure:

```text
Pathwise.Web/
│
├── app/
│   ├── page.tsx
│   ├── matches/
│   │   └── [matchId]/
│   │       └── page.tsx
│   └── layout.tsx
│
├── components/
│   ├── ui/
│   ├── matches/
│   ├── review/
│   └── charts/
│
├── lib/
│   ├── api/
│   ├── types/
│   └── utilities/
│
└── ...
```

The exact structure may evolve naturally.

Avoid introducing global state management unless actual requirements justify it.

Prefer normal React/Next.js data flow first.

---

# 7. Frontend Design System

Pathwise will use:

* Tailwind CSS for styling,
* shadcn/ui as the initial component foundation.

The purpose of shadcn/ui is to accelerate construction of polished interfaces without adopting a large opinionated UI framework.

Generated/copied components remain part of the Pathwise codebase and may be adapted where useful.

Pathwise should develop its own visual identity rather than appearing like an untouched component-library demo.

---

# 8. API Design

The frontend and backend communicate through conventional REST endpoints.

Initial endpoints may resemble:

```text
GET  /api/player
GET  /api/matches
POST /api/matches/fetch

GET  /api/matches/{matchId}
GET  /api/matches/{matchId}/analysis
POST /api/matches/{matchId}/analysis
```

These routes are illustrative rather than frozen contracts.

The API should expose Pathwise-oriented response models rather than Riot DTOs.

---

# 9. Riot Integration Boundary

Riot API interaction should sit behind a narrow application-facing abstraction.

Conceptually:

```csharp
public interface IRiotClient
{
    Task<Account> GetAccountAsync(...);

    Task<IReadOnlyList<string>> GetRecentMatchIdsAsync(...);

    Task<RiotMatch> GetMatchAsync(...);

    Task<RiotTimeline> GetTimelineAsync(...);

    Task<RankedInformation?> GetRankedInformationAsync(...);
}
```

Exact signatures and models should be decided during implementation.

Important principles:

* Riot DTOs remain in Infrastructure.
* HTTP retry/rate-limit handling belongs in Infrastructure.
* API credentials never enter frontend code.
* Riot request details should not spread throughout the application.

---

# 10. Data Ingestion Pipeline

Retrieval and analysis are separate operations.

Initial ingestion flow:

```text
User requests latest matches
        ↓
Resolve Riot account
        ↓
Retrieve match IDs
        ↓
Check local persistence
        ↓
Fetch missing match data
        ↓
Fetch missing timeline data
        ↓
Persist raw responses
        ↓
Normalize required information
        ↓
Mark match ready for analysis
```

Repeated fetches should avoid unnecessary Riot requests for already stored immutable match data.

---

# 11. Raw Data Strategy

Raw Riot payloads should be retained.

Conceptually:

```text
RawMatchData
RawTimelineData
```

Each record should include enough metadata to understand:

* Riot match identifier,
* retrieval timestamp,
* payload type,
* potentially source/API version information where useful.

Raw payloads form the source evidence from which normalized and derived data can be regenerated.

Do not prematurely normalize every field Riot provides.

Only normalize data Pathwise currently needs.

---

# 12. Persistence Layers

Pathwise effectively has three levels of persisted information.

## Level 1 — Source data

Raw Riot responses.

Example:

```text
RawMatch
RawTimeline
```

---

## Level 2 — Normalized game data

Pathwise representations required for querying and analysis.

Possible entities:

```text
Match
Participant
TimelineEvent
TimelineSnapshot
```

The exact amount of normalization should be determined empirically.

Avoid creating dozens of relational tables solely to mirror Riot JSON.

---

## Level 3 — Derived analysis

Outputs generated by Pathwise.

Examples:

```text
GameAnalysis
ReviewWindow
Observation
Recommendation
MetricResult
SignalResult
```

Derived information should carry an analysis version.

---

# 13. Analysis Versioning

Analysis output should include an algorithm/model version.

Example:

```text
AnalysisVersion = 1
```

When analytical behavior changes materially, the version may increment.

This allows Pathwise to:

* identify stale analyses,
* reanalyze stored matches,
* compare algorithm changes,
* reproduce historical behavior,
* avoid ambiguity about why two analyses differ.

The versioning mechanism should remain simple during MVP.

---

# 14. Analysis Pipeline

The central Pathwise pipeline is:

```text
Raw Riot data
      ↓
Game reconstruction
      ↓
Time-indexed game state
      ↓
Metrics
      ↓
Signals
      ↓
Review-window detection
      ↓
Observations
      ↓
Recommendations
      ↓
Game analysis
```

Each stage should have a clearly understandable responsibility.

---

# 15. Game Reconstruction

The reconstruction layer transforms Riot events and frames into a representation Pathwise can reason about.

It should eventually allow queries conceptually similar to:

```text
GetStateAt(10:00)

GetStateBetween(12:30, 16:00)

GetEventsBetween(12:30, 16:00)
```

Possible state information includes:

* player gold,
* experience,
* level,
* jungle CS,
* position,
* inventory,
* kills/deaths/assists,
* objective state,
* opponent-jungler state.

The implementation should reflect what Riot's actual timeline data reliably supports.

Do not invent unavailable precision.

---

# 16. Metrics

Metrics represent measurable characteristics.

Potential examples:

```text
GoldDifferential
ExperienceDifferential
JungleCsDifferential
FarmRate
KillParticipation
DeathFrequency
ObjectiveParticipation
EconomyRate
```

Metrics should primarily answer:

> What measurable thing changed?

Metric calculations should be deterministic and unit tested.

---

# 17. Signals

Signals interpret combinations or changes in metrics.

Potential examples:

```text
LeadContractionSignal
FarmRateDropSignal
DeathWhileAheadSignal
ObjectiveLossSignal
HighInteractionSignal
LowEconomyWindowSignal
```

Signals answer:

> Did something potentially meaningful occur?

A signal should expose the evidence that caused it to trigger.

Avoid opaque scoring systems during MVP.

---

# 18. Review Window Detection

Review windows identify periods worth examining.

Conceptually:

```text
Signals
   ↓
Group by temporal proximity
   ↓
Evaluate significance
   ↓
Select approximately 2–4 important windows
```

Review-window selection should favor:

* explainability,
* meaningful change,
* evidence density,
* relatively distinct periods.

The system should be able to explain why a window was selected.

Example:

```text
13:10–17:45

Selected because:
- large gold-lead contraction,
- farm-rate reduction,
- repeated combat activity,
- neutral objective lost.
```

The exact ranking algorithm should evolve through real-game validation.

---

# 19. Observations

Observations convert analytical evidence into structured meaning.

Example:

```text
Type:
LeadContraction

Window:
13:10–17:45

Evidence:
Gold differential changed from +1040 to +210.
Player jungle CS increased by 22.
Opponent jungle CS increased by 41.
Player participated in four kills.
Enemy team secured dragon.
```

Observations should remain largely deterministic.

---

# 20. Recommendations

Recommendations address:

> What could I have done differently?

Recommendations are inherently less certain than facts.

They should therefore:

* include supporting evidence,
* avoid claiming certainty where none exists,
* distinguish strong opportunities from speculative alternatives,
* remain traceable to observations.

Example:

```text
Possible alternative:

After the fight at 14:05, resetting and clearing the
available bottom-side camps before playing toward dragon
would have provided guaranteed economy while preserving
positioning for the next objective.
```

Recommendation sophistication should grow incrementally.

---

# 21. AI Integration Boundary

AI is intentionally outside the MVP's critical path.

Future architecture may introduce:

```text
IAiGameInterpreter
```

Conceptually:

```text
GameAnalysis
    ↓
Structured AI input
    ↓
AI interpretation
    ↓
Natural-language coaching
```

The AI layer should receive curated structured information such as:

* review windows,
* observations,
* metrics,
* event sequences,
* recommendation candidates.

It should not normally receive an unfiltered Riot payload.

The application must remain useful if no AI provider is configured.

---

# 22. Historical Analysis

Individual-match review remains the MVP priority.

However, all persisted games should form a reusable historical dataset.

Future services may calculate:

```text
HistoricalProfile
ChampionProfile
RollingPerformanceWindow
PersonalBenchmark
ImprovementTrend
```

Historical information may eventually contribute to single-match analysis.

Example:

```text
Your jungle CS at 15 in this game was below your
median across the previous 25 comparable games.
```

A dedicated historical dashboard is not required for MVP.

---

# 23. External Benchmarking

Comparison against higher-ranked players is a desired future capability.

Examples may include:

* Emerald vs Diamond jungle patterns,
* champion-specific rank benchmarks,
* patch-specific performance ranges.

This will require deliberate benchmark-data collection and normalization.

It is not an MVP requirement.

Do not introduce benchmark abstractions until an actual implementation needs them.

---

# 24. Testing Strategy

Testing effort should concentrate on logic whose incorrect behavior would invalidate analysis.

## Domain/Application tests

These receive the highest testing emphasis.

Examples:

```text
Given an initial +900 gold advantage
and a final +150 advantage,
lead contraction is detected.
```

```text
Given substantially reduced farm rate
during a defined window,
FarmRateDropSignal is produced.
```

```text
Given overlapping significant signals,
a review window is generated.
```

---

## Fixture-based tests

Real Riot match payloads should eventually be preserved as frozen fixtures.

Example:

```text
fixtures/
└── matches/
    ├── kha-kayn-loss-001/
    │   ├── match.json
    │   └── timeline.json
    └── ...
```

Fixture games can act as regression tests for the analysis engine.

This becomes increasingly important as analytical logic evolves.

Where necessary, fixtures should be sanitized before committing.

---

## Infrastructure tests

Test important boundaries such as:

* Riot response mapping,
* persistence,
* migrations,
* malformed responses,
* rate-limit/error behavior.

Avoid excessive mocking solely for coverage.

---

## API tests

Test important HTTP workflows and contracts.

Controller/endpoint tests should remain proportionate.

---

## Frontend tests

Frontend testing should be selective.

Prioritize:

* important user interactions,
* meaningful conditional rendering,
* analysis display behavior,
* failure/loading states.

Avoid large quantities of low-value snapshot tests.

---

# 25. Logging

Use structured ASP.NET Core logging.

Important events include:

* application startup,
* Riot retrieval attempts,
* Riot failures,
* rate limiting,
* match import success/failure,
* analysis start/completion/failure,
* unexpected source-data conditions.

Avoid logging:

* Riot API credentials,
* unnecessarily large raw payloads,
* sensitive configuration.

Raw match data belongs in persistence rather than application logs.

---

# 26. Configuration and Secrets

Configuration should use standard .NET configuration mechanisms.

Development secrets such as the Riot API key should use an appropriate local secrets mechanism or environment configuration.

Secrets must never be:

* committed to Git,
* embedded in frontend JavaScript,
* returned through Pathwise API responses,
* written to logs.

---

# 27. Database Migrations

EF Core migrations should be used from the beginning.

Schema evolution should therefore be explicit and reviewable.

During early MVP development, destructive migration decisions are acceptable only when deliberately approved.

AI coding agents should not independently introduce expensive or destructive persistence migrations without approval.

---

# 28. Developer Experience

The initial development experience may use two processes:

```text
Backend:
dotnet run

Frontend:
npm run dev
```

A simpler one-command startup may be added later if useful.

Docker should not be introduced solely to orchestrate local development.

Scripts may assume Windows where this meaningfully simplifies personal development.

---

# 29. Package Management

Use normal ecosystem tooling.

Frontend dependencies should be added deliberately rather than opportunistically.

Avoid dependencies for functionality easily implemented with platform/framework capabilities.

Significant runtime dependencies should have a clear purpose.

AI coding agents should explain the need for major new dependencies before adding them.

---

# 30. Architectural Decision Records

Consequential decisions should be documented under:

```text
docs/decisions/
```

Examples:

```text
0001-use-dotnet-backend.md
0002-use-nextjs-frontend.md
0003-retain-raw-riot-data.md
0004-separate-ingestion-from-analysis.md
```

Not every implementation choice deserves an ADR.

Use ADRs for decisions that:

* meaningfully constrain future architecture,
* would be costly to reverse,
* or would otherwise require future developers to rediscover the reasoning.

---

# 31. AI Developer Boundaries

AI coding agents are expected to make normal low-level implementation decisions autonomously.

Examples include:

* local variable naming,
* small refactoring choices,
* private method organization,
* ordinary component decomposition,
* straightforward test implementation.

They should request approval before materially changing:

* project architecture,
* persistence strategy,
* database schema in destructive ways,
* public API contracts,
* major dependencies,
* security model,
* core analysis semantics,
* repository layout,
* supported scope.

When unsure whether a decision is consequential, prefer asking.

---

# 32. Deliberately Deferred Decisions

The following should not be prematurely frozen:

* charting library,
* AI provider,
* AI SDK,
* global frontend state library,
* cloud provider,
* deployment architecture,
* authentication system,
* multi-user design,
* background job framework,
* external benchmark-storage strategy,
* native desktop packaging,
* messaging infrastructure.

These decisions should be made only when corresponding requirements exist.

---

# 33. Initial Vertical Slice

The first implementation milestone should prove the complete data path without sophisticated analysis.

Target workflow:

```text
Configure Riot identity
        ↓
Configure Riot API key
        ↓
Open Pathwise
        ↓
Fetch recent matches
        ↓
Persist match data
        ↓
Persist timeline data
        ↓
Identify jungle games
        ↓
Display recent matches
        ↓
Open one match
        ↓
Display basic factual match information
```

This validates:

* project structure,
* frontend/backend communication,
* Riot integration,
* configuration,
* persistence,
* migrations,
* basic mapping,
* local developer workflow.

Complex review analytics should not be built until this vertical slice works reliably.

---

# 34. Second Vertical Slice

Once ingestion works, take one real stored jungle match and build the first analysis path.

Target:

```text
Stored match
    ↓
Identify player + opposing jungler
    ↓
Reconstruct time-based state
    ↓
Expose state at key timestamps
    ↓
Calculate first small metric set
    ↓
Render chronological game progression
```

This should use a known game—preferably a Kha'Zix game we can manually reason about—as the first analytical fixture.

---

# 35. Architectural Litmus Tests

When considering a new implementation choice, ask:

### Does this make the analysis more trustworthy?

If not, its value should be questioned.

### Does this make future analytical changes easier?

This is particularly important around raw-data retention and deterministic reconstruction.

### Are we solving a real current requirement?

Avoid speculative architecture.

### Can we explain why the system produced this result?

Opaque analytics should be treated cautiously.

### Would changing this decision later be expensive?

If yes, give it additional scrutiny before implementation.

---

# 36. Current Architecture Summary

```text
                         PATHWISE

                    Next.js + TypeScript
                    Tailwind + shadcn/ui
                             │
                             │ REST
                             ▼
                      ASP.NET Core API
                             │
                             ▼
                       Application
                             │
                  ┌──────────┴──────────┐
                  ▼                     ▼
               Domain               Infrastructure
                  │                     │
                  │                  ┌──┴─────────┐
                  │                  ▼            ▼
                  │               EF Core      Riot API
                  │                  │
                  │                  ▼
                  │                SQLite
                  │
                  ▼
            Analysis Pipeline

     Reconstruction → Metrics → Signals
              → Review Windows
              → Observations
              → Recommendations

                       Future:
                          │
                          ▼
                   AI Interpretation
```

The central architectural objective is simple:

> **Pathwise should be able to explain its analysis from persisted evidence, while allowing the analytical model to evolve without rebuilding the rest of the application.**
