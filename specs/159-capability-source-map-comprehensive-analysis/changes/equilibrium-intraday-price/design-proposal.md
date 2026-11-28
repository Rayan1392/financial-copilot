# Design Proposal: equilibrium card price from today's intraday trades

Status: Approved — Gate A and Gate B by the owner on 2026-11-28 («با هر دو مورد موافقم، گیت A و B تأیید است»)

## Feature Folder

`159-capability-source-map-comprehensive-analysis/changes/equilibrium-intraday-price`

## Request

The owner (2026-11-28): the price on the equilibrium card should come from the intraday price. The daily price is not
updated before 19:00 Tehran; after that the daily table may be read. On some Wednesdays the daily price is not updated
even by 19:00.

## Current behavior

`ValuationGaugeQuoteReader` (Infrastructure) calls `PersistedMarketDataProvider.GetCanonicalTradePricesAsync` with a
freshness of `MarketViews:StaleAfterMinutes` (15). Today's latest `IntradayTradeSnapshots` row counts only when it was
received in the last 15 minutes; otherwise the latest `DailyInstrumentTrades` row is used. The TSETMC intraday poll stops
when the market closes, so from about 12:45 until today's daily row arrives the card shows yesterday's price, and the
distances «تا میانه … رشد» are measured from it. The watchlist has no freshness limit and shows today's last price.

## Proposed rule (equilibrium card only)

Tehran time is a fixed UTC+03:30. `T` is `ValuationGaugeCards:DailyPriceReadyTehran` (default `19:00`).

1. Before `T`: today's latest intraday snapshot (last traded price above zero, with a trading time), however old.
2. From `T`: today's daily row when it exists; otherwise today's latest intraday snapshot (late Wednesday update).
3. No intraday snapshot today (Thursday, Friday, holiday, no trade in the symbol): the latest daily row, as today.

Source labels stay as they are (`IntradayToday` for an intraday snapshot, `LatestDailyFallback` for a daily row), and so
does the price time under the card. No frontend change.

## Design

- The choice is a pure Application policy, `ValuationGaugeQuoteSelection.Select(todayIntraday, latestDaily, now,
  dailyPriceReady)` in `FinancialCopilot.Application/FinancialData/ValuationGauges/`, unit tested without a database.
- `PersistedMarketDataProvider.GetSessionTradeCandidatesAsync(isin, now)` reads the two candidates for one ISIN: today's
  latest intraday snapshot without a freshness limit and the latest daily row up to today, both not after `now`.
- `ValuationGaugeQuoteReader` reads the candidates and returns the policy's choice. The shared
  `GetCanonicalTradePricesAsync` and its 15-minute rule stay unchanged for every other caller.
- `ValuationGaugeCardsOptions.DailyPriceReadyTehran` ("HH:mm", validated at startup).

## Decisions (answered 2026-11-28)

- OQ-187: the 19:00 threshold is a setting — yes.
- OQ-188: Feature 125 industry comparison (`CyclicalWavesV2RelativeValuationInputReader`) has the same 15-minute rule; it
  stays unchanged and is recorded in `specs/backlog.md`.

## Risks

- Before 19:00 on a trading day the card shows the last intraday price even if the intraday poll stopped early in the
  session; the price time under the card shows how old it is (the same as the watchlist).
- TSETMC's daily job runs at 17:00 Saturday to Tuesday and at 20:00 on Wednesday (`TsetmcPollingWorker`). Until today's
  daily row exists, rule 2 keeps the intraday price, so the job time does not matter for this card.

## Alternatives rejected

- Dropping the freshness limit in `GetCanonicalTradePricesAsync`: it would also change Feature 125 ratios, which the
  owner did not ask for.
- Always preferring today's daily row when it exists, without `T`: the owner set the 19:00 rule; a partial daily row
  written earlier would otherwise replace the intraday price.
