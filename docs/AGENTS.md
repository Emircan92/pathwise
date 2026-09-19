# AGENTS.md

## Purpose

This file defines how AI coding agents should work within the Pathwise repository.

The goal is to use AI as the primary implementation developer while preserving human control over product direction, architecture, consequential technical decisions, and final acceptance.

Pathwise should be treated as a real maintainable software project, not as disposable generated code.

Before beginning non-trivial work, read:

* `docs/MVP.md`
* `docs/ARCHITECTURE.md`
* this file

These documents define the current product scope, architecture, and working expectations.

---

# 1. General Working Model

The human developer acts primarily as:

* product owner,
* technical lead,
* architecture reviewer,
* code reviewer,
* acceptance tester.

The AI agent is expected to:

* implement scoped work,
* propose reasonable technical solutions,
* write and maintain tests,
* follow existing architecture,
* surface risks and ambiguity,
* preserve project quality,
* avoid expanding scope without approval.

The agent should exercise judgment, but not silently make consequential product or architectural decisions.

---

# 2. Plan Before Non-Trivial Work

Before making changes for any non-trivial task, provide a short implementation plan.

The plan should identify:

* the intended outcome,
* the main areas likely to change,
* the proposed approach,
* relevant tests or validation,
* any important assumptions.

Keep the plan concise.

Tiny fixes such as typo corrections or obvious one-line changes do not require a formal plan.

Do not begin a broad or architectural implementation without first making the intended approach clear.

---

# 3. Approval Gates

Stop and ask for approval before making changes that materially affect:

* architecture,
* database schema design in a consequential way,
* destructive database migrations,
* persistence strategy,
* public API contracts,
* repository structure,
* major production dependencies,
* security model,
* secrets/configuration strategy,
* core analysis semantics,
* supported product scope,
* technology stack.

Examples include:

* introducing a new architectural layer,
* replacing SQLite,
* changing REST contracts consumed by the frontend,
* adopting a state-management framework,
* introducing background processing infrastructure,
* adding a major runtime framework,
* changing how analysis results are fundamentally calculated,
* deleting or restructuring persisted data.

Do not proceed with these changes merely because they appear technically preferable.

Explain the issue and request approval first.

---

# 4. Small Autonomous Decisions

The agent may make ordinary, reversible implementation decisions without approval.

Examples include:

* private method organization,
* local variable naming,
* ordinary class/component decomposition,
* test naming,
* internal helper extraction,
* minor refactoring,
* implementation details inside an agreed architectural boundary,
* file placement within an already established module,
* small configuration choices that do not alter architecture.

Use good engineering judgment.

If a small decision is notable or non-obvious, mention it in the completion summary.

---

# 5. Handling Uncertainty

For consequential ambiguity:

> Ask rather than guess.

For low-risk ambiguity:

> Choose a reasonable interpretation and continue.

Do not block progress over trivial decisions.

Do not invent requirements when ambiguity could materially affect product behavior or architecture.

If proceeding with an assumption, state it clearly in the final summary.

---

# 6. Scope Discipline

Implement only the requested task and the minimum supporting work required to complete it correctly.

Do not opportunistically fix unrelated issues.

If unrelated problems are discovered:

* leave them unchanged unless they block the requested work,
* mention them separately in the final summary,
* explain briefly why they may deserve follow-up.

Avoid turning small tasks into broad cleanup efforts.

---

# 7. Architecture Discipline

Follow `docs/ARCHITECTURE.md`.

In particular:

* Next.js owns frontend presentation and interaction.
* ASP.NET Core owns backend APIs.
* Application logic belongs outside HTTP handlers.
* Domain logic must remain independent of infrastructure.
* Riot API DTOs must remain at the integration boundary.
* The frontend must not call Riot directly.
* Analytical logic must remain outside the frontend.
* Raw Riot data should remain recoverable.
* Ingestion and analysis should remain separable.
* Deterministic analysis should precede future AI interpretation.

Do not bypass boundaries for convenience.

If an existing architectural rule appears harmful or inadequate, raise the issue rather than silently violating it.

---

# 8. Product Discipline

Follow `docs/MVP.md`.

The product's primary question is:

> What should I have done differently?

Features should contribute directly or indirectly toward answering that question.

Avoid building functionality solely because analytics products typically contain it.

For MVP work, prioritize:

* useful game reconstruction,
* trustworthy evidence,
* review-worthy periods,
* understandable explanations,
* traceable recommendations.

Do not expand into deferred areas such as broad rank benchmarking, multi-user support, live assistance, or large historical dashboards unless explicitly requested.

---

# 9. Dependency Policy

Do not add new production/runtime dependencies without approval.

When proposing a runtime dependency, explain:

* what problem it solves,
* why built-in/framework capabilities are insufficient,
* expected maintenance or architectural implications.

Development and test dependencies may be added without prior approval when:

* they are conventional,
* narrowly scoped,
* clearly justified,
* and do not meaningfully change project architecture.

Mention new dependencies in the completion summary.

Avoid dependency sprawl.

---

# 10. Testing Expectations

Every completed task should include appropriate validation.

Before declaring work complete:

* run relevant automated tests,
* run applicable builds,
* run linting/static analysis where configured,
* verify the affected workflow when practical.

Report exactly what was run and whether it passed.

Do not claim success without verification.

If a test or validation step cannot be run:

* state that clearly,
* explain why,
* describe the remaining risk.

---

# 11. Testing Priorities

Testing effort should focus on meaningful behavior.

Prioritize:

* domain logic,
* analytical calculations,
* signal detection,
* review-window behavior,
* Riot mapping boundaries,
* persistence behavior,
* important API workflows,
* meaningful frontend interactions.

Avoid low-value testing purely to increase coverage numbers.

Avoid excessive snapshot testing.

For analytics work, prefer deterministic fixture-based regression tests when possible.

---

# 12. Real Match Fixtures

When real Riot payloads are used as fixtures:

* preserve them as stable regression inputs where useful,
* sanitize data if needed before committing,
* avoid mutating fixtures during test execution,
* keep fixture purpose understandable.

Fixture-based tests should help answer:

> Did this analytical change unintentionally alter known behavior?

---

# 13. Git Behavior

Do not create commits unless explicitly asked.

Do not push changes unless explicitly asked.

Do not create branches unless explicitly asked.

Do not create pull requests unless explicitly asked.

The human developer controls Git workflow by default.

The agent may inspect Git state and diffs as needed.

When completing work, leave changes reviewable in the working tree unless instructed otherwise.

---

# 14. Documentation Discipline

`docs/MVP.md` and `docs/ARCHITECTURE.md` define fundamental project truths.

Do not casually rewrite or expand them.

Update them only when the implemented or approved direction materially changes what those documents state.

For consequential architecture decisions, prefer adding an ADR under:

```text
docs/decisions/
```

when the reasoning is likely to matter later.

Do not create ADRs for routine implementation details.

Documentation should remain concise enough to stay useful.

Avoid documentation inflation.

---

# 15. Code Quality

Prefer:

* clear naming,
* straightforward control flow,
* small focused units,
* strong typing,
* explicit behavior,
* maintainable abstractions,
* readable tests.

Avoid:

* speculative abstraction,
* unnecessary generic frameworks,
* premature optimization,
* large "manager" or "service" classes containing unrelated responsibilities,
* clever code that is difficult to review,
* duplicated analytical logic,
* leaking infrastructure models into the domain.

Favor boring code when boring code is easier to understand and maintain.

---

# 16. Comments

Use comments when they explain:

* why a non-obvious decision exists,
* an important Riot-specific constraint,
* an analytical assumption,
* behavior that may look incorrect without context.

Do not use comments to narrate obvious code.

Prefer expressive code over excessive commentary.

---

# 17. Error Handling

Handle expected failure cases explicitly.

Examples include:

* Riot API failure,
* rate limiting,
* missing or malformed source data,
* unsupported game modes/roles,
* missing timeline data,
* persistence failure,
* analysis failure.

Do not silently swallow failures.

Avoid returning misleading success states.

Error messages should provide enough context for local debugging without exposing secrets.

---

# 18. Logging

Use structured logging.

Log meaningful operational events, not noise.

Good examples:

* match fetch started/completed,
* Riot request failed,
* match skipped because already stored,
* analysis started/completed,
* unexpected data condition detected.

Do not log:

* Riot API keys,
* secrets,
* entire raw payloads without a strong reason.

---

# 19. Secrets

Never commit secrets.

The Riot API key must not appear in:

* source files,
* frontend code,
* committed configuration,
* logs,
* test fixtures.

Use standard local configuration mechanisms.

If a task appears to require committing or exposing a secret, stop and ask.

---

# 20. Frontend Expectations

Pathwise should feel polished enough for regular use, but pixel-perfect design is not required.

Frontend work should prioritize:

* clarity,
* readable hierarchy,
* explanation-first presentation,
* responsive layout,
* useful loading/error states,
* progressive disclosure of detail.

Avoid unnecessary frontend complexity.

Do not introduce global state management unless actual requirements justify it.

Do not duplicate backend analytical logic in TypeScript.

---

# 21. Backend Expectations

Keep HTTP endpoints thin.

Business and analytical behavior should live in appropriate Application or Domain components.

Do not let controllers/endpoints become orchestration dumping grounds.

Keep Riot-specific mapping and transport concerns in Infrastructure.

Prefer explicit interfaces at meaningful boundaries, not interface-per-class architecture.

---

# 22. Analysis Expectations

Analytical output must be explainable.

Where possible, every derived signal or recommendation should be traceable to evidence.

Prefer:

> Lead contracted from +900 to +150 while farm rate dropped.

over:

> Tempo score = 61.

Avoid opaque scoring systems unless explicitly justified and approved.

Distinguish:

* fact,
* derived observation,
* recommendation.

Do not present uncertain tactical interpretation as objective truth.

---

# 23. AI Integration

Do not introduce LLM integration unless explicitly requested.

Future AI integration should consume structured Pathwise analysis rather than raw Riot payloads wherever practical.

Core application usefulness must not depend on AI availability.

Do not build speculative AI abstractions prematurely.

---

# 24. Performance

Do not optimize prematurely.

However:

* avoid obviously wasteful Riot requests,
* reuse persisted immutable match data,
* avoid reparsing/recalculating unnecessarily where simple caching or persistence already exists,
* keep analysis deterministic and rerunnable.

Prefer correctness and clarity before optimization.

---

# 25. Completion Summary

At the end of every non-trivial task, provide a concise completion summary with:

## What changed

Describe the implemented behavior.

## Why

Explain the main reasoning where not obvious.

## Files touched

List the meaningful files or areas changed.

## Validation

Report:

* tests run,
* builds run,
* linting/static analysis run,
* manual verification if applicable,
* whether each passed or failed.

## Risks / assumptions

Mention:

* assumptions made,
* known limitations,
* uncertainty,
* unverified behavior.

## Needs attention

Call out:

* approval needed,
* discovered unrelated issues,
* suggested follow-up work.

Keep the summary concise and useful for code review.

---

# 26. Interaction Style

Be concise and execution-focused by default.

Explain architectural, analytical, or non-obvious decisions when they matter.

Do not over-explain routine implementation details unless asked.

Do not hide uncertainty behind confident language.

When proposing alternatives, explain the meaningful tradeoff rather than presenting unnecessary option lists.

---

# 27. Definition of Done

A task is not complete merely because code was written.

A task is complete when:

* requested behavior is implemented,
* architecture remains respected,
* relevant tests exist,
* validation has been run,
* failures are disclosed,
* scope has not silently expanded,
* consequential decisions have been approved,
* documentation is updated only where necessary,
* the result is ready for human review.

---

# 28. Core Rule

When choosing between speed and preserving trustworthy, understandable software:

> Prefer trustworthy, understandable software.

When choosing between guessing and asking about a consequential decision:

> Ask.

When choosing between adding scope and completing the requested task well:

> Complete the requested task well.
