# Market summary index unavailable when index snapshots use TSETMC identity

Date: 2026-07-22

## Symptom

The main chat page left context panel showed:

```text
وضعیت کلی بازار
داده شاخص در دسترس نیست.
```

while still showing a recent market-summary update time.

## Root Cause

`MarketSummaryService` populated `MarketSummary.Indices` only when `DailyIndexSnapshots` joined to
`TradingInstruments.ExternalInstrumentId` values from the legacy StockMarketDB named-index catalog.

The active direct TSETMC feed can persist index snapshots under TSETMC `InstrumentCode` identities
instead. In that mode, the summary endpoint can still compute `asOf` from latest stock quotes, but
the governed index lookup returns zero rows, causing the UI to display "index data unavailable".

## Fix

Add explicit TSETMC instrument-code aliases to the governed named-index catalog and let
`MarketSummaryService` match daily index snapshots by either:

- legacy StockMarketDB `ExternalInstrumentId`
- direct-feed TSETMC `InstrumentCode`

The regression seed now stores the total-index snapshot with TSETMC code `32097828799138957` so
the endpoint test covers the production failure mode.

## Expected Behavior

`GET /api/v1/market/summary` should return at least the governed total-index observation when the
latest available index snapshot is linked through TSETMC `InstrumentCode`, and the main chat
context panel should render the index value instead of the unavailable message.
