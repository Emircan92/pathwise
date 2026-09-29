# ADR 0008: Narrative Interpretation V1

## Status

Accepted and implemented.

## Context

Pathwise needs a coherent account of a selected review period without allowing an AI model to replace deterministic reconstruction or invent missing game state. Narrative Interpretation V1 is interpretation, not coaching: it identifies significant developments, concurrent stories, investigation moments, and evidence limits.

## Decision

Application projects the authoritative selected final window into `NarrativeInterpretationInputV1`. The browser supplies only window bounds and reconstruction/detector versions; the server reloads the locally stored Riot payloads, recomputes the review, and resolves that exact window. A canonical SHA-256 fingerprint binds the model output to the complete projected input.

`INarrativeInterpretationProvider` is the only provider seam. Infrastructure initially implements it with the OpenAI Responses API through `HttpClient`, using configurable model and reasoning effort. Provider output is structured and is rejected unless its schema, fingerprint, evidence references, time ranges, enum values, sizes, and required uncertainty are valid.

Every factual or synthesized claim cites one or more stable input evidence IDs. Model output is never authoritative game state and is not persisted in V1. The UI requests one selected period explicitly and caches a successful result only in page-session component state.

Narrative Interpretation V1 receives no periodic position samples, raw coordinates, inferred paths, or inferred map regions. This is a deliberate V1 safety limitation because sparse samples do not establish continuous movement. A future calibrated spatial-evidence layer may revisit that restriction.

## Consequences

Pathwise remains useful when narrative interpretation is disabled, unconfigured, or unavailable. Deterministic tests can cover projection, fingerprints, validation, endpoint behavior, and UI lifecycle without live model calls. Recommendation, decision-quality judgment, alternative actions, champion-specific coaching, counterfactuals, whole-match interpretation, and interpretation persistence remain separate future work.
