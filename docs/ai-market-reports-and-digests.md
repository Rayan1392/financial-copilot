# AI Market Reports and Personal Digests

Feature 096 renders persisted Feature 095 market-pulse snapshots and Features 084/090/092 insight events into evidence-bound Persian narratives. The LLM is only a renderer: every factual sentence must cite an evidence item and every numeric value must exist in that item's `numericValues`. Invalid, unsafe, unavailable, or timed-out model output is replaced with a deterministic report built from the same evidence bundle.

## API

Public, anonymous report reads:

```text
GET /api/v1/market-reports/latest
GET /api/v1/market-reports/history?from=2026-07-01&to=2026-07-14&page=1&pageSize=20
GET /api/v1/market-reports/{reportId}
```

Actor-authenticated personal digest operations:

```text
GET  /api/v1/digests/me/latest
GET  /api/v1/digests/me/history
GET  /api/v1/digests/me/{reportId}
POST /api/v1/digests/me/generate
```

The generation body is optional in intent but should be sent as JSON:

```json
{ "publishNotification": false }
```

Setting `publishNotification` to `true` creates an idempotent `PersonalMarketDigestReady` `NotificationIntent`. Feature 097 remains responsible for durable Telegram delivery, retry, suppression, and deduplication. Regeneration does not automatically send another notification.

## Report semantics

- Scopes: `PublicMarket`, `IntradayMarket`, and `PersonalDigest`.
- States: `Pending`, `Generated`, `Fallback`, `Failed`, and `Superseded`.
- A changed/corrected pulse or insight set creates a new revision and retains the superseded report.
- Personal evidence snapshots the canonical actor's followed-symbol membership at generation time. It never infers holdings, exposure, P/L, suitability, price targets, or trade instructions.
- Responses expose evidence, evidence hash, citations, source freshness, confidence, caveats, report revision, generated time, and published time.
- Model prompts contain minimized evidence only; provider credentials and raw transient provider payloads are never persisted.

## Billing and entitlement

Personal generation uses the `AiQuery.PersonalDigest` plan capability and operation code. Pro, Plus, and Premium plans are enabled; Free is not. Successful validated AI rendering commits the reservation once. Provider failure or rejected output publishes the deterministic fallback and releases the reservation without a usage-ledger charge.

## Scheduling and Telegram

`MarketReportWorker` runs after eligible current pulse snapshots. Intraday snapshots produce `IntradayMarket`; a final pulse produces `PublicMarket`. Durable generation keys and report leases make repeated workers idempotent, with bounded retry/backoff and a terminal failed state after the configured attempt limit.

Telegram commands reuse the same application boundary:

```text
/report  latest published public report
/digest  generate or replay the actor's evidence-bound digest
```

The `mreport.sources.v1:{reportId}` callback lists evidence sources and freshness. Telegram's existing message splitter handles long narratives, and the response includes the corresponding web report path.

## Configuration

`MarketReports` configures cadence, evidence selection limits, daily manual digest limit, lease/attempt policy, evidence retention, transient model-payload retention, and eligible segments. Evidence is retained independently from transient model payloads; Feature 096 stores only normalized model metadata, not raw credentials or transport payloads.
