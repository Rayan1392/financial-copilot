# Tasks — Market Microstructure Event Detectors

## 1. Boundaries and Governed Definitions

- [x] Extend Feature 084 `InsightEvent` and detector contracts; do not create Telegram-specific detection or a parallel market-event table.
- [x] Reuse Features 030/054/064 canonical instruments, quotes, trades, market-session state, source priority, and freshness; document missing source fields before implementation.
- [x] Make this feature the owner of deterministic large-trade, queue, buyer-power, real-money-flow, volume, and trading-value event definitions used by 091/093/094/095/096.
- [x] Version every detector definition, input dataset contract, formula, baseline, threshold, session scope, and evidence schema.

## 2. Metric and Event Semantics

- [x] Define large-trade amount/value aggregation, side classification, minimum absolute/relative thresholds, aggregation window, and treatment of cancel/correction records.
- [x] Define real buyer/seller power from eligible real-person buy/sell count and volume, including zero-denominator and incomplete-client-type handling.
- [x] Define retail/institutional inflow/outflow and “smart money” only as a disclosed deterministic methodology; prohibit unsupported actor-intent claims.
- [x] Define buy/sell queue formation, strengthening, weakening, release, and collection using allowed price, queue value/volume, duration, and session transitions.
- [x] Define volume/trading-value anomaly baselines (lookback sessions, minimum observations, median/mean choice, corporate-action/outlier rules) and rarity score.
- [x] Define configurable thresholds by market segment/instrument class and a governed default; record the effective values with every event.
- [x] Define false-positive controls: minimum liquidity, minimum persistence, stale/incomplete snapshot rejection, repeated-event cooldown, and hysteresis.

## 3. Persistence and Evidence

- [x] Persist results through `InsightEvent` with canonical company/instrument, event time/window, detector code/version, importance/severity/confidence, source freshness, and immutable evidence.
- [x] Include exact input values, units, thresholds, baseline window/statistics, sample size, market-session state, provider/source ids, calculation time, and formula version for reproducibility.
- [x] Use stable identity `(DetectorCode, Version, Instrument, TradingDate, Window, SourceEventIdentity)` and Feature 084 unique deduplication policy.
- [x] Add only indexes/extensions needed for detector queries; do not duplicate raw trade/quote history or user delivery state.
- [x] Represent provider corrections as a superseding/corrected event or versioned recomputation with audit link; never silently rewrite delivered evidence.

## 4. Detection Processing

- [x] Implement one pure detector per governed event family behind `IInsightDetector`, with shared baseline/session/freshness readers.
- [x] Support market-wide execution and bounded company/watchlist scopes over the same detectors; user scope affects selection, never formulas.
- [x] Trigger from canonical trade/quote completion events where available and use bounded scheduled scans otherwise; prevent processing partial source batches as final.
- [x] Partition by instrument/trading window, apply bounded concurrency and distributed lease/queue ownership, retry transient reads, and dead-letter poison inputs.
- [x] Reject out-of-order/stale observations and make reruns idempotent; recomputation must reproduce identical evidence for identical inputs/version.

## 5. Contracts and Consumers

- [x] Expose new event types through existing market/symbol insight feeds with filters; no detector-specific public execution endpoint unless an admin pattern requires it.
- [x] Provide deterministic contracts for Feature 091 conditions, Feature 093 radar selection, Feature 094 filter results, Feature 095 aggregation, and Feature 096 evidence bundles.
- [x] Keep event text informational and disclose methodology; never label an event a guaranteed signal or claim hidden institutional intent.
- [x] Ensure Feature 097 is the only notification handoff and Feature 099 is the only user alert-history projection.

## 6. Security and Observability

- [x] Protect admin/backfill triggers with existing policies and service authentication; market-wide detection contains no user identity.
- [x] Emit detector latency, source lag, eligible instruments, event rate, suppression reason, baseline insufficiency, duplicates, corrections, and failures by detector/version.
- [x] Trace source batch/observation to detector run and `InsightEvent`; alerting must detect stalled detectors, abnormal event spikes, stale inputs, and poison backlog.

## 7. Tests and Acceptance Scenarios

- [x] Unit-test formulas with exact fixtures, zero denominators, missing client types, short baselines, outliers, corrections, session transitions, and threshold edges.
- [x] Reproducibility-test identical inputs/version produce identical identity, score, and evidence; changed version remains historically distinguishable.
- [x] Integration-test canonical source reads, Feature 084 persistence/feed filters, correction handling, retry/idempotency, and consumer contracts.
- [x] Concurrency-test duplicate source events/workers yield one persisted event.
- [x] Given sufficient fresh history and a threshold breach, when a detector runs, then one event contains the exact current value, baseline, threshold, formula version, and source evidence.
- [x] Given stale/incomplete data or insufficient history, when detection runs, then no positive event is invented and the skip/freshness reason is observable.

## Completion Gate

- [x] Keep tasks unchecked until formulas are product-approved and deterministic, replay, correction, concurrency, and cross-feature contract tests pass.
