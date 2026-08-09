# Feature 118 Tasks — Conversational Capability Registry and Query Frame

## [x] Task 1 — Inventory the Active Capability Surface

Reconcile active V1/V2 routes, tools, result contracts, feature flags, and frontend examples.

Acceptance:

- Every currently executable capability has an owner and route.
- Advertised but non-executable examples are identified.
- `Clarification` and `Unknown` are excluded from the business capability list.

## [x] Task 2 — Define Versioned Registry Contracts

Define capability, localized alias/example, slot, output, data requirement, precedence, and suggestion metadata.

Acceptance:

- Contracts are framework/provider-neutral.
- Capability codes are stable and serialized safely.
- Feature disablement and historical response compatibility are specified.

## [x] Task 3 — Implement Registry Validation

Validate duplicate codes/aliases, route existence, slot consistency, localization completeness, and precedence collisions.

Acceptance:

- Invalid production configuration fails fast.
- Validation has deterministic unit tests.
- No user prompt can mutate registry definitions.

## [x] Task 4 — Implement Query Normalization

Normalize Persian/Arabic characters, whitespace, ZWNJ, punctuation, digits, casing, and approved financial notation.

Acceptance:

- Original text remains unchanged for persistence.
- Normalization is deterministic and idempotent.
- Presentation vocabulary is separate from entity vocabulary.

## [x] Task 5 — Define and Validate QueryInterpretation

Implement the structured frame, provenance, evidence, confidence, missing slots, and unsupported parts.

Acceptance:

- Unknown fields/codes and oversized collections are rejected.
- Explicit, conversation-derived, policy-defaulted, and model-proposed values are distinguishable.

## [x] Task 6 — Build Hybrid Capability Interpretation

Combine deterministic recognizers with an optional schema-constrained LLM proposal.

Acceptance:

- Deterministic known routes do not require model availability.
- Model output cannot bypass registry validation.
- Malformed/timeout behavior maps through Feature 117.

## [x] Task 7 — Define Routing Precedence and Confidence Policy

Centralize conflict cases and configurable thresholds.

Acceptance:

- Scanner vs lookup, trend vs point lookup, analysis vs metric, and gauge vs P/S lookup cases are covered.
- Low-confidence behavior is deterministic and does not guess.

## [x] Task 8 — Generate Prompt and Metadata Projections

Produce bounded agent/tool and client metadata from enabled definitions.

Acceptance:

- Disabled capabilities disappear from generated guidance.
- Storage/provider details are not exposed.
- Prompt size has a tested upper bound.

## [x] Task 9 — Add Registry and Interpretation Telemetry

Record registry version, candidates, winning confidence, evidence categories, latency, and validation failures using bounded dimensions.

Acceptance:

- Raw prompt text is not a metric label.
- Correlation with Feature 117 outcome is possible.

## [x] Task 10 — Add Paraphrase and Adversarial Tests

Cover Persian, English, mixed language, misspacing, punctuation, prompt injection, and routing conflicts.

Acceptance:

- The monthly trend golden set includes `چارت روند فروش فولاد` and never extracts `چارت` as an entity.
- Tests assert frame content, provenance, confidence band, and candidate ordering.

## Completion Gate

Keep the feature unchecked until the registry is authoritative, interpretation is schema-validated, conflict policy is tested, and no LLM-proposed executable identifier can bypass governance.
