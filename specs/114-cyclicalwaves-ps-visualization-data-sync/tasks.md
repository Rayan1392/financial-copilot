# Feature 114 Tasks (Updated)


Tasks - CyclicalWaves P/S Visualization Data Sync

All tasks are specification-only and remain unimplemented.

## [ ] Task 1 - Sanitize and Freeze the Three Provider Fixtures

Create token-free contract fixtures for:

/api/ps/circle-chart-data/{symbolIsin};

/api/ps-data/{symbolIsin};

/api/ps/{symbolIsin}.

Acceptance:

No authorization value or browser-only header is committed.

The history fixture records the verified facts: 1,124 points, 1,124 unique IDs, 1,116 unique dates,eight duplicate-date groups, 2021-03-27 first date, and 2026-07-29 last date.

Unknown additive JSON fields do not break deserialization.

The disclosed token is explicitly listed in the operational rollout checklist as requiringrotation.


## [x] Task 2 - Document Verified Gauge Contract

Acceptance criteria: - Six provider buckets are preserved. - Segment
visual order is a,b,c,d,e,f. - Segments are equal-width 30-degree histogram bins. - Population is
used for exact/display percentages only. - No local quantile calculation is performed. - Segment `a`
uses `start..min`, segments `b..e` divide `min..max` into four equal numeric intervals, and segment
`f` uses `max..end`. - `start/end/min/max/avg` remain separate provider facts. - Needle uses current
`ps_ratio`, interpolates within the matching segment, and clamps only outside `start..end`.


## [ ] Task 2 - Verify Gauge Boundary, Arc, and Needle Semantics

Capture at least three same-symbol sets containing both API responses and the vendor-rendered gauge.Document:

the bucket order represented by a through f;

the meaning/order of start, min, avg, max, and end;

whether arcs are equal-width or value-proportional;

the interpolation and out-of-range clamping formula;

which current value drives the needle;

the semantic relationship, if any, among gauge close, ps_ratio, and ps-data.close.

Acceptance:

A deterministic reference algorithm reproduces all captured gauges within an agreed pixel/numerictolerance.

Any unresolved semantic keeps the gauge component non-renderable.

No field-name-based guess is promoted to production behavior.

## [ ] Task 3 - Define Provider Contract Bounds and Error Semantics

Document required/optional fields, null/zero behavior, decimal precision, maximum point count,maximum response bytes, 404/204 behavior, authentication behavior, 429/Retry-After, and whether anyverified pagination or conditional request exists.

Acceptance:

Contract decisions are backed by fixtures or provider documentation.

No unsupported cursor, fromDate, ETag, or pagination parameter is invented.

Oversized and truncated responses are explicitly rejected/classified.

## [ ] Task 4 - Define Provider-Neutral Application Contracts

Add contracts for:

eligible-company scope enumeration and conflict reporting;

one-company snapshot sync;

one-company history sync;

bounded backfill and recurring sync;

persisted snapshot/history reads;

component quality/completeness/freshness inputs;

normalized sync outcomes and error classifications.

Acceptance:

Domain/Application contracts do not expose HTTP DTOs, EF rows, chart-library objects, or browserconcepts.

Cancellation, provider provenance, correlation, and bounded warnings are included where relevant.

## [ ] Task 5 - Extend the Typed CyclicalWaves Client

Add narrow operations for the three endpoints using the existing authenticated client pipeline.

Acceptance:

Token refresh, timeout, retry, jitter, circuit breaker, and telemetry reuse provider-foundationbehavior.

Only required server headers are sent.

401 after one refresh, no-data, 429, timeout/network, 5xx, oversized payload, invalid JSON, andcancellation are distinct outcomes.

Secrets and full payloads never appear in application-facing exceptions.

## [ ] Task 6 - Implement Eligible Scope Normalization

Read CompanyId and SymbolIsin from NoavaranEligibleCompanies, restricted to MarketId values `037c69ad-f519-419f-ae62-59003b6b2428` and `a3ccb30a-caed-4f26-a84a-ac0eb8c78c76`. Normalize the scope and report identity issues. Use SymbolIsin for every CyclicalWaves P/S provider request.

Acceptance:

Blank/invalid ISINs are skipped and counted.

Identical rows are deduplicated.

one-company/multiple-ISIN and one-ISIN/multiple-company conflicts are rejected and counted.

No canonical company is inserted or updated.

Enumeration order is deterministic.

dryRun returns bounded previews and aggregate counts without provider calls.

## [ ] Task 7 - Add Persistence Rows, Constraints, and Migration

Create/configure/migrate:

CompanyPsGaugeSnapshots;

CompanyPsHistoryPoints;

CompanyPsSeriesSyncStates.

Acceptance:

Snapshot uniqueness is (ProviderName, CompanyId, ObservationDate).

History uniqueness is (ProviderName, CompanyId, ProviderPointId).

Observation date remains non-unique.

Active-series/date indexes support chart reads.

Fixed-precision decimal, 64-bit count, string-length, and JSON bounds are explicit.

Foreign keys do not cascade-delete financial history.

Migration metadata is verified on the real database provider.

## [ ] Task 8 - Implement Canonical Hashing

Define invariant normalized hashes for the combined snapshot and full history series.

Acceptance:

Hashes exclude fetch timestamps and JSON property/array order noise.

History hash uses normalized points in deterministic order plus declared metadata.

Equal normalized content produces equal hashes across cultures and process restarts.

Hashing never includes tokens or authorization headers.

## [ ] Task 9 - Implement Component Validation and Quality Classification

Validate identity, required fields, dates, decimals, bucket counts/totals, response limits, declaredmetadata, boundary semantics, and cross-endpoint consistency.

Acceptance:

Zero remains distinct from missing.

Bucket total <= 0 is non-renderable/rejected for gauge use.

Multiple same-date points are retained with a warning, not treated as duplicates.

Ratio sanity/tolerance limits are configuration-governed.

Gauge, current-values, and history statuses are independently representable.

## [ ] Task 10 - Implement Atomic Gauge/Current Snapshot Upsert

Fetch gauge and current values as one logical attempt and persist a combined snapshot only afterboth endpoint contracts validate structurally. Separately classify whether its gauge is renderable.

Acceptance:

TTM, Forward, and GaugeClose remain separate fields.

Structurally valid but semantically unverified/invalid boundaries persist with an explicitnon-renderable gauge status.

Bucket counts and provider boundaries are stored exactly.

Observation date comes from the validated current-values response.

Same normalized snapshot is unchanged/idempotent.

One-component failure preserves the last renderable snapshot and records the partial attempt insync state/run detail.

Concurrent retries cannot create duplicate rows.

## [ ] Task 11 - Implement Full-History Upsert and Active-Series Reconciliation

Persist every point by provider point ID and switch the active series only after whole-responsevalidation succeeds.

Acceptance:

The supplied fixture persists exactly 1,124 active points.

All eight duplicate-date groups remain available.

Same ID/same content is idempotent.

Same ID/conflicting content quarantines the refresh and never overwrites accepted evidence.

A complete metadata-consistent later response soft-inactivates absent IDs atomically.

Metadata-mismatched, invalid, oversized, cancelled, or partial responses leave the previous activeset unchanged.

Ordered reads are stable by date then provider point ID.

## [ ] Task 12 - Implement the Bounded Sync Coordinator

Coordinate snapshot refresh and lower-frequency full-history refresh with per-company isolation.

Acceptance:

Snapshot and history cadences use elapsed time and are independently configurable.

Max concurrency, request delay, company count, response size, point count, and run duration areenforced.

One company's failure does not fail other companies.

Cancellation stops scheduling new companies and safely resolves the in-flight transaction.

Deterministic ordering and idempotency make interrupted runs resumable.

No destructive truncate/replace operation is used.

## [ ] Task 13 - Add Renewable Distributed Lease Protection

Use the existing distributed lease mechanism for worker and DataAdmin-triggered runs.

Acceptance:

Multiple replicas cannot overlap the same logical run.

The owner renews the lease before expiry.

Lease loss cancels the run and prevents further commits by the former owner.

Lease contention/loss is visible in run status and telemetry.

## [ ] Task 14 - Add the Configuration-Gated Worker

Register a BackgroundService that delegates only to the coordinator.

Acceptance:

Disabled by default.

Startup validates all sync options.

Restart behavior follows an explicit missed-run policy and does not automatically stampede theprovider.

History cadence is not based on an in-memory run counter.

Worker shutdown honors cancellation.

## [ ] Task 15 - Add DataAdmin Operations

Expose protected operations for:

dry-run scope;

bounded backfill/resume;

recurring-style sync;

one-company retry;

snapshot-only retry;

history-only retry;

run/status/failure inspection.

Acceptance:

Existing DataAdmin policy and authenticated rate limits apply.

Manual behavior while worker-disabled follows configuration.

Responses contain bounded codes/counts and never raw provider payloads or secrets.

Operations are correlation-aware and safe to retry.

## [ ] Task 16 - Add Telemetry, Health, and Freshness Inputs

Instrument provider calls, synchronization, data quality, lease behavior, and local data state.

Acceptance:

Health distinguishes disabled, auth failure, provider unavailable/rate-limited, missing data,stale data, incomplete backfill, and healthy state.

Metrics do not use provider point IDs, ISINs, or arbitrary symbols as labels.

Logs never contain tokens or full response bodies at information level.

Snapshot source date and last successful sync timestamps are queryable by spec 115.

## [ ] Task 17 - Add Unit and Contract Tests

Cover:

all three DTOs and additive fields;

exact decimal round-trip;

bucket totals and zero/missing behavior;

verified boundary/needle reference fixtures;

latest-value mapping;

normalized hashing independent of array order/culture;

count/date-range validation;

duplicate dates and duplicate/conflicting IDs;

identity mismatch and malformed/oversized responses;

provider failure classification.

## [ ] Task 18 - Add Persistence, Concurrency, and Worker Integration Tests

Cover:

real-provider migration/index metadata;

snapshot and history idempotency;

1,124-point fixture and eight duplicate-date groups;

atomic active-series switch and soft-inactivation;

conflict quarantine and previous-series preservation;

partial failure preservation;

bounded company isolation and cancellation;

concurrent upsert safety;

renewable lease/no overlap/lease loss;

DataAdmin authorization;

worker disabled/enabled/restart behavior;

chart-ready read with zero outbound HTTP calls.

## [ ] Task 19 - Produce Operations and Security Runbooks

Document:

secret/environment setup and token rotation;

provider request budget and approved concurrency;

first backfill, bounded resume, and verification procedure;

stale/no-data/auth/rate-limit troubleshooting;

lease-loss recovery;

history conflict and soft-inactivation investigation;

readiness gates required before enabling spec 115.

Acceptance:

No credential is committed.

Production enablement requires successful sample-company parity, complete initial backfill, andverified gauge semantics.
