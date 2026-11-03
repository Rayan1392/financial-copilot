# Feature 125 Design Review

> **Superseded scope notice (2026-08-17):** This review approved an `IndustryId`-keyed cohort. The
> amended design uses exact `GroupId`/`GroupTitle` identity and requires a new architecture review.

## Review Status

APPROVED

## Executive Summary

The remediated design is implementation-ready. It preserves the locked normalization and watch
rules, uses the existing canonical NADPCO catalog, prevents raw-value comparison, keeps calculation
off the AI path, and defines deterministic provider, statistical, publication, ranking, semantic,
concurrency, and audit contracts.

## Verified Existing Architecture

The repository uses FinancialIngestionDbContext, provider-scoped normalized catalog rows, existing
CyclicalWaves authentication/resilience and raw-payload conventions, leased bounded Workers, persisted
data-sync activity, Tehran timezone fallback, Feature 118 capability definitions, Feature 119
resolution outcomes, and Feature 120 optimistic conversational state. Feature 114 owns the existing
P/S visualization sync and is explicitly not reinterpreted by this design.

## Provider Contract Assessment

P/S reuses Feature 114 acquisition and adds one provider-fact projection: gauge close is current P/S
and gauge avg is historical average P/S; BoundaryAverage remains visualization data. P/E and
equilibrium use the locked endpoints and exact field mappings. All three contracts define bounded
payloads, decimal validation, identity checks, no-data/failure outcomes, freshness, provenance, and
reuse of existing authentication, retry, rate-limit, telemetry, and raw-payload policies.

## Data Model Assessment

The design specifies versioned calculation, metric, member, watch-state, and transition records with
canonical company/industry keys, source observation hashes, source barriers, membership hash,
readiness/status, exclusion reasons, algorithm versions, and unique keys. It does not mutate Feature
114 semantics or create a second industry taxonomy.

## Statistical / IQR Assessment

The algorithm is deterministic: decimal arithmetic, R7 interpolation, explicit samples 0–4, inclusive
bounds, IQR == 0, 1.5 multiplier, invalid-value exclusion before quartiles, minimum two clean
observations, and persisted IQR-R7-1.5-v1.

## Ranking Assessment

All members remain visible; only members with at least one classifiable metric receive a financial rank.
0/0 is unranked and does not consume Top-N. The persisted total order is positive count descending,
nullable normalized metrics ascending with nulls last, coverage descending, and CompanyId ascending.
Global rank precedes Top-N and pagination. Default and maximum result limits are bounded and validated.

## Industry Identity Assessment

Membership is the active NADPCO canonical catalog joined by Company.IndustryId = Industry.Id.
Provider scope and ExternalId are part of identity; title is display-only. Missing classification,
inactive members, source-uncovered members, and the distinction between membership and metric
availability are normative.

## Semantic Layer Assessment

Four versioned Feature 118 capabilities are specified with slots, precedence, route, outputs, and
bounded read contracts. Feature 119 outcomes cover resolution, ambiguity, missing, not-found, wrong
industry, and cross-industry pairs. Feature 120 carries pending clarification state. The LLM cannot
provide formulas, SQL, ranks, averages, or colors.

## Daily Processing / Readiness Assessment

The design uses Tehran business dates, source barriers, configured freshness, explicit Pending/Ready/
Published/Inconclusive/Failed states, atomic calculation publication, and no mixed-generation complete
snapshot. Historical rows are written even when source values are unchanged.

## Idempotency / Concurrency Assessment

Leases are distinct for ingestion and calculation. Date/version and member unique keys, source hashes,
monotonic publication, barrier-based no-op retries, lower-readiness protection, and calculation-id
watch references prevent duplicate streak advancement. Corrected data creates an auditable new version.

## Long-Term Watch Assessment

The durable states are NotWatching, EntryPending, Watching, and ExitPending. Inconclusive is a
persisted evaluation outcome that pauses, rather than resets, the current streak. Entry and exit are
strictly below/above 100, require three valid published snapshots by default, are mutually exclusive,
and are idempotent for same-date recalculation.

## Historical Auditability Assessment

Published calculations preserve source observation ids/hashes, watermarks, payload hashes, membership
hash, algorithm/rank versions, boundaries, exclusions, publication state, and watch transition
evidence. A corrected version never destroys the prior evidence.

## Configuration Assessment

Cadence, freshness, IQR multiplier, result limits, and entry/exit streak lengths have explicit keys,
defaults, validation ranges, and maximums. Algorithm and rank versions are persisted so configuration
changes cannot silently rewrite historical calculations.

## Edge Case Assessment

The design covers small samples, no clean observations, all non-positive values, zero-IQR data,
metric-specific outliers, missing versus invalid metrics, 0/0 members, nullable ties, stable
pagination, provider failures, stale/partial inputs, catalog changes, ambiguous titles, invalid
membership, cross-industry pairs, concurrent retries, corrected source data, exact 100%, and
inconclusive watch days. Required fixture and regression coverage is listed in the design.

## Remaining Gaps

None. P/E and equilibrium response fixtures must be added as part of implementation because those
provider slices are new; their endpoint shapes and business mappings are already fixed by the
approved design contract and do not require another product decision.

## Product Decisions Still Required

None.

## Final Verdict

APPROVED
