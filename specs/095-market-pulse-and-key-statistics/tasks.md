# Tasks — Market Pulse and Key Statistics

## 1. Canonical Definitions

- [x] Reuse Features 030/054/064 source-priority, instruments, trades, quotes, and session state plus Feature 092 flow/queue definitions.
- [x] Make this feature owner of immutable market-wide pulse snapshots; Feature 096 owns narrative and Feature 097 owns delivery.
- [x] Govern included markets/instrument classes and exact formulas/units for transaction value, small-trade value, equity/fixed-income real-money flow, queue counts/values, breadth, and industry leaders/laggards.
- [x] Define trading session states `PreOpen`, `Open`, `Intermission`, `Closed`, `Holiday`, `Unknown`, and which measures are valid/partial in each.
- [x] Define weekly/monthly comparison windows using completed trading sessions, minimum sample size, correction policy, and source freshness thresholds.

## 2. Snapshot Model and Persistence

- [x] Define `MarketPulseSnapshot` with trading date, capture/session state, sequence/cadence slot, partial/final/corrected status, definition version, facts, evidence, source watermarks, and generated timestamp.
- [x] Store deterministic facts in typed fields/read model and immutable evidence with included/excluded counts, provider ids, units, and cutoff times.
- [x] Enforce unique `(TradingDate, SessionState/CadenceSlot, DefinitionVersion, Revision)` and one designated current/final revision without overwriting prior published snapshots.
- [x] Add indexes for latest, trading-date history, state, and final report; retain snapshots per analytics policy and preserve published revisions.
- [x] Handle late/corrected source data by creating a new revision linked to the superseded snapshot and exposing correction metadata.

## 3. Calculation and Scheduling

- [x] Implement pure calculators for every fact, breadth bucket, industry score, and baseline comparison with zero/missing-data behavior.
- [x] Calculate only from a consistent source watermark/cutoff and record incomplete datasets; do not combine observations from incompatible capture times silently.
- [x] Schedule configurable intraday cadence during valid sessions and one final post-close snapshot after required sources settle; holidays are represented once with an observable state/reason.
- [x] Use a PostgreSQL transaction-scoped advisory lease, bounded single-worker concurrency, idempotent slot key, bounded retry/backoff, exhausted-attempt logging, and safe current/final-slot restart.
- [x] Distinguish live partial-day values from final values in all contracts; never label a partial snapshot final.

## 4. API and Consumer Contracts

- [x] Specify `GET /api/v1/market-pulse/latest` and paginated history with date/state/final filters, typed facts, comparison windows, freshness, evidence, revision, and partial flag.
- [x] Return explicit unavailable/partial/stale facts rather than zeros; use stable units and Persian labels without converting persisted meaning.
- [x] Provide evidence-bundle contracts consumed by Feature 096 and deterministic event/filter consumers without duplicating calculations.
- [x] Define current-revision reads so clients do not serve superseded final snapshots; no second cache was introduced.

## 5. Security, Observability, and Tests

- [x] Keep generation worker-only, protect public reads with the existing authenticated market policy and actor rate limit, and keep user-specific state out of snapshots.
- [x] Emit slot/revision, watermark, included/excluded instruments, partial/final status, retry exhaustion, and failures through structured logs.
- [x] Unit-test formula behavior, zero/missing inputs, breadth, industry ties, session state, and comparison windows.
- [x] Integration-test immutable revisions, slot idempotency, current selection, partial/unavailable behavior, correction, pagination contract, and authentication.
- [x] Given a valid intraday cutoff, when calculation runs, then one partial snapshot contains reproducible facts and exact source freshness.
- [x] Given late corrected data after publication, when recalculated, then a linked new revision becomes current and prior evidence remains immutable.

## Completion Gate

- [x] Formulas/scope are recorded in the story; cadence, finalization, correction, freshness, concurrency, API, migration, and architecture checks pass.
