# Feature 125 User Story Review

> **Superseded scope notice (2026-08-17):** This review predates the `GroupId`/`GroupTitle` cohort
> correction. The amended user story is ready for review and is not approved by this historical
> verdict.

## Review Status

APPROVED

## Executive Summary

The remediated user story is implementation-ready against the approved Stage 2
design. It now expresses the locked formulas, provider mappings and failure
outcomes, source-fact provenance and immutability, canonical membership,
benchmark and ranking rules, calculation lifecycle, freshness barrier, atomic
publication, historical correction, semantic routing, Feature 119/120
clarification behavior, configurable watch thresholds, operational policies,
configuration validation, and deterministic fixture coverage.

No product-owner decision remains. The story does not change the approved
business rules, introduce raw peer valuation, authorize provider calls on the AI
path, or introduce buy/sell recommendations.

## Design Traceability Review

Traceability is complete and maps the acceptance criteria to all relevant
approved design sections:

- §1: locked formulas, decimal arithmetic, and invalid-input rules;
- §2–§3: repository boundaries, canonical NADPCO identity, membership,
  inactive/unclassified/moved companies;
- §4: Feature 114 P/S projection, P/E/equilibrium contracts, validation,
  failure outcomes, provenance, immutability, and resilience/logging policy;
- §5: Tehran calculation date, source barrier, freshness, statuses, readiness,
  visibility, and atomic publication;
- §6–§7: R7/IQR, classification, outliers, rank order, limits, and pagination;
- §8: version identity, current selection, correction, audit, and idempotency;
- §9: complete watch state machine, configurable thresholds, neutral days,
  inconclusive pause, and same-date identity;
- §10: capabilities, precedence, persisted read contract, and Feature 119/120;
- §11: leases, worker behavior, activity evidence, and configuration;
- §12–§13: required fixture coverage and non-goals.

Feature 114 is preserved as the P/S acquisition owner and `BoundaryAverage` is
explicitly excluded from the relative baseline. Features 118–120 are referenced
with their required capability, resolution, clarification, versioning, and
replay rules.

## Acceptance Criteria Review

AC-01–07 deterministically define canonical resolution, pair behavior,
capability registration, executor boundaries, precedence, and clarification.

AC-08–15 define the exact provider endpoints/fields and P/S reuse, all required
validation and provider failure outcomes, bounded payload and logging behavior,
the source fact fields, immutable source identity, and calculation provenance.

AC-16–25 define exact normalization, quality handling, R7/IQR behavior,
minimum population, outlier/classification output, total ranking, `0/0`, result
limits, and stable pagination.

AC-26–32 define Tehran calculation date, complete source barrier and freshness,
all five durable calculation statuses, partial-generation handling, daily
history, atomic publication, version identity, monotonic current selection, and
watch references.

AC-33–37 remove the previous fixed-three ambiguity. Entry and exit use their
configured thresholds, cover values 1, 2, 3, and greater than 3, enforce
mutual exclusion, clear both counters on neutral valid days, pause on
Inconclusive, and prevent same-date duplicate advancement.

AC-38–44 define deterministic normal/diagnostic read fields, audit evidence,
lease and worker behavior, operational observability, exact configuration
ranges/defaults, and the no-recommendation boundary.

All criteria use deterministic Given/Then behavior or explicit required
contract fields. None relies on an LLM to calculate, rank, classify, or choose
a formula.

## Provider Contract Review

The story covers the approved P/S, P/E, and equilibrium mappings and reuse of
the existing CyclicalWaves authentication/resilience stack. It requires valid,
additive-field, malformed, oversized, numeric, identity, 404/204, auth, 429,
timeout, network, and 5xx outcomes, with distinct readiness/quality results.
It also requires bounded audit retention, no ordinary-log payloads, bounded or
hashed telemetry labels, and immutable source provenance.

## Publication / Readiness Review

The story requires the Tehran business date, one source barrier per canonical
member/source kind, persisted selected observations and watermarks, freshness
validation, no mixed-generation publication, all five statuses, normal-AI
visibility only for complete Published data, atomic publication, safe retry,
monotonic current selection, daily historical rows, and auditable corrected
versions.

## Watch-State Review

The story requires valid Published days with three independently publishable
benchmarks, strict below/above 100 predicates, exact-100 neutrality, arbitrary
validated streak thresholds, EntryPending/ExitPending counters and transitions,
mutual exclusion, Inconclusive pause, and evaluation identity
`(IndustryId, CalculationId, EvaluationKind)`. Same-date corrections cannot
advance a streak twice.

## Semantic / Clarification Review

All four Feature 118 v1 capabilities, slots, route, precedence, canonical-ID
executor boundary, and LLM-input rejection are explicit. Feature 119 outcomes
and the feature-specific mismatch outcomes are explicit. Feature 120 pending
slot, candidate IDs, optimistic versioning, one-turn resume, replay
idempotency, and task-switch isolation are explicit.

## Operational / Configuration Review

The story includes separate ingestion/calculation leases, the approved date
lease key, single-date publication, bounded worker behavior, cancellation and
deadline, per-company isolation, existing retry/rate-limit/timeout reuse,
persisted activity evidence, bounded telemetry, and all approved option ranges,
defaults, startup validation, and version persistence.

## Edge Case Coverage

The dedicated fixture section covers provider and payload failures, exact
provider mappings, R7 sample sizes 2/3/4, zero IQR and inclusive bounds,
insufficient/all-invalid data, metric-specific outliers, partial coverage,
ties and pagination, stale/partial generations, corrections and concurrent
retries, catalog changes, resolution mismatches, cross-industry pairs, watch
thresholds 1 and greater than 3, Inconclusive pause, exact 100, and duplicate
prevention.

## Remaining Gaps

None identified against `design.md`, `design-review.md`, the Feature 125 README,
or the referenced Feature 114/118/119/120 contracts.

## Product Owner Questions

None.

## Final Verdict

APPROVED — `tasks.md` can be created next. This review authorizes only the next
specification stage; it does not authorize production code or migrations.
