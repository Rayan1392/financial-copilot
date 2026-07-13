# Tasks — Market Pulse and Key Statistics

## 1. Canonical Definitions

- [ ] Reuse Features 030/054/064 source-priority, instruments, trades, quotes, and session state plus Feature 092 flow/queue definitions.
- [ ] Make this feature owner of immutable market-wide pulse snapshots; Feature 096 owns narrative and Feature 097 owns delivery.
- [ ] Govern included markets/instrument classes and exact formulas/units for transaction value, small-trade value, equity/fixed-income real-money flow, queue counts/values, breadth, and industry leaders/laggards.
- [ ] Define trading session states `PreOpen`, `Open`, `Intermission`, `Closed`, `Holiday`, `Unknown`, and which measures are valid/partial in each.
- [ ] Define weekly/monthly comparison windows using completed trading sessions, minimum sample size, correction policy, and source freshness thresholds.

## 2. Snapshot Model and Persistence

- [ ] Define `MarketPulseSnapshot` with trading date, capture/session state, sequence/cadence slot, partial/final/corrected status, definition version, facts, evidence, source watermarks, and generated timestamp.
- [ ] Store deterministic facts in typed fields/read model and immutable evidence with included/excluded counts, provider ids, units, and cutoff times.
- [ ] Enforce unique `(TradingDate, SessionState/CadenceSlot, DefinitionVersion, Revision)` and one designated current/final revision without overwriting prior published snapshots.
- [ ] Add indexes for latest, trading-date history, state, and final report; retain snapshots per analytics policy and preserve published revisions.
- [ ] Handle late/corrected source data by creating a new revision linked to the superseded snapshot and exposing correction metadata.

## 3. Calculation and Scheduling

- [ ] Implement pure calculators for every fact, breadth bucket, industry score, and baseline comparison with zero/missing-data behavior.
- [ ] Calculate only from a consistent source watermark/cutoff and record incomplete datasets; do not combine observations from incompatible capture times silently.
- [ ] Schedule configurable intraday cadence during valid sessions and one final post-close snapshot after required sources settle; skip holidays with observable reason.
- [ ] Use distributed lease, bounded concurrency, idempotent slot key, retry/backoff, poison handling, and safe restart/backfill for missed slots.
- [ ] Distinguish live partial-day values from final values in all contracts; never label a partial snapshot final.

## 4. API and Consumer Contracts

- [ ] Specify `GET /api/v1/market-pulse/latest` and paginated history with date/state/final filters, typed facts, comparison windows, freshness, evidence, revision, and partial flag.
- [ ] Return explicit unavailable/partial/stale facts rather than zeros; use stable units and Persian labels without converting persisted meaning.
- [ ] Provide evidence-bundle contracts consumed by Feature 096 and deterministic event/filter consumers without duplicating calculations.
- [ ] Define cache invalidation/current-revision behavior so clients do not serve superseded final snapshots.

## 5. Security, Observability, and Tests

- [ ] Protect generation/backfill/correction endpoints with admin/service policies and rate-limit public history; no user-specific state belongs in snapshots.
- [ ] Emit cadence lag, calculation duration, input watermarks, included/excluded instruments, partial facts, revisions, correction count, and failures; alert on missing final snapshot or stale source.
- [ ] Unit-test every formula/unit, instrument scope, zero denominators, breadth, industry ties, session state, and comparison window.
- [ ] Integration-test immutable revisions, slot idempotency, latest/final selection, holiday/partial behavior, correction, pagination, and provider failure.
- [ ] Given a valid intraday cutoff, when calculation runs, then one partial snapshot contains reproducible facts and exact source freshness.
- [ ] Given late corrected data after publication, when recalculated, then a linked new revision becomes current and prior evidence remains immutable.

## Completion Gate

- [ ] Keep tasks unchecked until formulas/scope are approved and cadence, finalization, correction, freshness, concurrency, and API tests pass.
