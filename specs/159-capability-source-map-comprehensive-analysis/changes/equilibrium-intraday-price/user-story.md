# User Story: equilibrium card price from today's intraday trades

## Feature Folder

`159-capability-source-map-comprehensive-analysis/changes/equilibrium-intraday-price`

## Layers / Area

Layers: backend
Area: FinancialData | AI

## Priority

P2

## Complexity

S

## Depends On

- `159-capability-source-map-comprehensive-analysis/changes/standalone-valuation-gauge-cards` (released)
- `159-capability-source-map-comprehensive-analysis/changes/equilibrium-distance-from-price` (implemented)

## References

- `design-proposal.md` in this folder (Gate A and Gate B 2026-11-28; OQ-187 and OQ-188 answered)
- `docs/tsetmc-webservice-datasource.md` (intraday and daily trade tables)
- `docs/providers/cyclicalwaves-v2/README.md` (valuation gauge cards)
- Code: `ValuationGaugeQuoteReader` in `ValuationGaugeSnapshotReader.cs`, `PersistedMarketDataProvider`,
  `ValuationGaugeCardsOptions`, `ValuationGaugeCardsUseCase`

## Standards Source Of Truth

Obey `.claude/rules/*`. Do not copy rule text into this spec.

## User Story

As a shareholder looking at a symbol's equilibrium price range after the market closes,
I want the card to use today's last traded price,
so that the distances to the range are measured from today's price and not from yesterday's.

## Scope

- The price read for the equilibrium card (`IValuationGaugeQuoteReader`) only.
- New setting `ValuationGaugeCards:DailyPriceReadyTehran`, default `19:00`.

## Out Of Scope

- `GetCanonicalTradePricesAsync` and its 15-minute freshness for other callers, including Feature 125 industry
  comparison (backlog, OQ-188).
- Labels, card layout, frontend, Telegram text.
- A public-holiday calendar.

## Requirements

1. Tehran time is UTC+03:30. "Today" is the Tehran date of the request time.
2. An intraday candidate is today's latest `IntradayTradeSnapshots` row of the symbol's instruments with a last traded
   price above zero, a trading time and `ReceivedAt` not after the request time; latest by trading time, then by
   `ReceivedAt`. There is no freshness limit.
3. A daily candidate is the latest `DailyInstrumentTrades` row with a last traded price above zero, a trading date not
   after today and `SourceInsertedAt` not after the request time; latest by trading date, then by `SourceInsertedAt`.
4. Before `DailyPriceReadyTehran` the intraday candidate is used when it exists.
5. From `DailyPriceReadyTehran` the daily candidate is used when its trading date is today; otherwise the intraday
   candidate when it exists.
6. Without an intraday candidate the daily candidate is used, whatever its date.
7. Without either candidate the card has no quote, as today.
8. The intraday candidate keeps the label `IntradayToday` and its `ReceivedAt` as price time; the daily candidate keeps
   `LatestDailyFallback` and its `SourceInsertedAt`.
9. `DailyPriceReadyTehran` must be a valid `HH:mm` time; an invalid value stops startup with the message
   `DailyPriceReadyTehran must be a time HH:mm.`

## Acceptance Criteria

- AC-01: On a trading day at 14:00 Tehran with an intraday row from 12:29 and yesterday's daily row, the card shows the
  intraday price with `IntradayToday`.
- AC-02: At 19:30 with today's daily row, the card shows the daily price with `LatestDailyFallback`.
- AC-03: On a Wednesday at 19:30 with today's intraday row and only yesterday's daily row, the card shows the intraday
  price.
- AC-04: On a Thursday with no intraday row today, the card shows Wednesday's daily price.
- AC-05: At 10:00 with an intraday row from 09:58, the card shows that intraday price (unchanged from today).
- AC-06: A different `DailyPriceReadyTehran` (for example `20:30`) moves the switch to daily accordingly; an invalid value
  fails options validation.
- AC-07: Feature 125 comparison and other callers of `GetCanonicalTradePricesAsync` behave as before.

## Assumptions

- The TSETMC intraday poll stores the last snapshot of the session; the latest intraday row of the day is the closing
  last traded price or close to it.
