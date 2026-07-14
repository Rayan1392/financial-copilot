# Order 92 Design — Market Microstructure Event Detectors

## Decision

Order 92 extends Feature 084. It does not add a second event table or any notification path.
Pure, provider-neutral domain policies emit candidates; the infrastructure adapter reads canonical
market rows and maps candidates to the existing `InsightEvent` aggregate and feeds. Feature 097
remains the only notification handoff and Feature 099 remains the alert-history owner.

Definitions are owned by `Financial.Insights.Microstructure` and use policy version
`microstructure-v1`, detector-local version `1`, and evidence schema
`microstructure-evidence-v1`. The stable event identity is:

```text
MM:{DetectorCode}:{Policy/DetectorVersion}:{Instrument}:{TradingDate}:{Window}:{SourceEventIdentity}
```

A provider correction must have a new source identity, set `IsCorrection`, and identify the
superseded source identity. It therefore creates linked immutable evidence rather than silently
changing an already delivered event.

## Canonical-source audit

| Evidence | Current canonical row | Runtime status |
|---|---|---|
| Instrument/company/market | `TradingInstrumentRow`, `NormalizedCompanyRow` | Available |
| Cumulative intraday volume/value/transactions | `IntradayTradeSnapshotRow` | Available |
| Historical daily volume/value | `DailyInstrumentTradeRow` | Available |
| Per-trade value/volume and aggressor side | None | Suppressed until canonical ingestion exists |
| Real/legal buyer and seller counts/volumes/values | None | Suppressed until canonical ingestion exists |
| Allowed min/max price and order-book queue depth | None | Suppressed until canonical ingestion exists |
| Cancel/correction/supersession metadata | Domain input contract only | Supported when a canonical provider supplies it |

The missing fields are not inferred from total volume, price movement, provider names, or analysis
content. Consequently, volume and trading-value anomalies can run immediately. Large-trade,
buyer/seller-power, real-money-flow, and order-queue policies are implemented and fixture-tested,
but produce an observable suppression reason against today's incomplete canonical rows.

## Governed formulas and defaults

All money values use the canonical provider's rial-valued traded-value field. Configuration lives
under `MarketMicrostructure`; a market-segment override may replace absolute large-trade,
money-flow, queue, and anomaly thresholds without changing formulas.

- Large trade: `LargestTradeValue >= max(50,000,000,000,
  median(daily traded value) * 0.05)`. Side is `Buy`, `Sell`, or explicitly `Unknown`. A missing
  per-trade value suppresses the event. Corrections are new, linked observations.
- Buyer/seller power: `(real buy volume / real buyer count) /
  (real sell volume / real seller count)`. Buyer power is at least `1.5`; seller power is at most
  `1 / 1.5`. Missing counts/volumes or zero denominators suppress the event.
- Real money flow: `real buy value - real sell value`. Emit inflow/outflow only when absolute net
  flow reaches `max(20,000,000,000, median(daily traded value) * 0.02)`. It is descriptive retail
  flow and never called “smart money”. Institutional values are evidence only.
- Queue events: a queue must be at the canonical allowed price, have value at least
  `10,000,000,000`, and persist for `120` seconds. A zero/non-material previous queue forms; a
  change of at least `20%` strengthens or weakens; a material-to-non-material transition releases,
  or collects only when canonical execution evidence confirms collection.
- Volume/value anomaly: current cumulative value divided by the positive historical median over
  the last `20` sessions. At least `10` observations are required and the default trigger is `2x`.
  Median limits isolated outliers; zero/non-positive history is excluded. Corporate-action
  adjustment is not fabricated—when adjusted canonical history is unavailable, operators can
  suppress the affected instrument/window and replay after canonical correction.
- Rarity: empirical percentage of baseline observations less than or equal to the current value.
  Importance/confidence reuse Feature 084 deterministic scoring.

## False-positive and replay controls

- Only the latest observation per instrument in a bounded batch is evaluated.
- Intraday observations outside 08:45–12:45 local trading time are rejected; sources without an
  intraday time retain their explicit intraday classification.
- Source lag over 15 minutes, incomplete evidence, zero denominators, and short baselines suppress
  positive events.
- Queue duration and 20% hysteresis prevent transient/repeated queue chatter.
- Exact source identity plus the database unique deduplication key makes replays idempotent. The
  repository recovers the distributed unique-key race when workers overlap.
- The worker scans every 60 seconds, uses bounded reads, retries transient batch failures three
  times, and isolates a throwing detector/input while other instruments continue. The next cadence
  is the retry owner; there is no second raw-event queue or duplicate delivery state.
- Optional company IDs and `Take` on `InsightDetectionContext` provide market-wide or bounded
  watchlist/company selection without changing formulas.

## Evidence and consumers

Every emitted event contains detector code/version, evidence schema, canonical instrument,
trading date/window, provider source identity, source freshness, exact current/baseline/threshold
values, sample size, correction link, importance, severity, and confidence. Numeric evidence is
invariant-culture text for deterministic replay.

Existing market/symbol/industry feeds filter the six new `InsightType` values. Feature 091 reads
the exact evidence labels `buyer_power_ratio`, `net_real_money_flow`, and side-specific
`queue_value`. Features 093–096 consume the same event types and evidence schema rather than
reimplementing formulas.

## Operations

The meter `FinancialCopilot.MarketMicrostructure` publishes observations, signals, suppression
reasons, corrections, poison-input failures, source lag, and detector duration. Operational alerts
should alarm on no observations/signals over expected sessions, excessive lag/failures, and an
event-rate spike relative to the normal segment baseline. Admin replay continues to use the
existing DataAdmin-protected Feature 084 generation endpoint; no public detector execution route
was added.
