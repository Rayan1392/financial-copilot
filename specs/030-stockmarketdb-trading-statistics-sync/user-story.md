# StockMarketDB Trading Statistics Synchronization

## User Story

As a FinancialCopilot operator, I want registered instruments, stock trading statistics, and
market indices synchronized from the read-only `StockMarketDB` SQL Server source into
PostgreSQL so scanner answers and market summaries can use fresh market values with auditable
fallbacks.

## Scope

- Add a dedicated `StockMarketDb` read-only provider adapter.
- Normalize the following source datasets (authoritative mapping):
  - Daily indices (شاخص روزانه): `Tse.IndexNew`, scoped by named-index `InstrumentRef`.
  - Intraday / in-day indices (شاخص لحظه‌ای - بین‌روز): `tse.IndexB1LastDay`.
  - Daily trades (آمار معاملات روزانه): `TSE.TradeRefined` (`TradeOneDay`) — one price per
    trading day per instrument.
  - Intraday / in-day trades (آمار معاملات لحظه‌ای - بین‌روز): `tse.Trade`.
  - Instruments: `Tse.Instrument`.
- Resolve named market indices in `Tse.IndexNew` by `InstrumentRef`:
  - شاخص کل — `36423CB8-D33B-47AD-89D4-06FA49592CBA`
  - شاخص کل فرابورس — `1B32B991-F48A-4F7E-9C0C-328D0B093EA5`
  - شاخص بازده نقدی و قیمت — `B27FA320-194F-4710-8D12-277E245D33C5`
  - شاخص ۵۰ شرکت فعال‌تر — `47CE7543-C052-4C44-BF0D-29281818FCA5`
  - شاخص قیمت (هم‌وزن) — `42FCE63E-6CEB-405B-9179-78606C210D86`
  - شاخص کل (هم‌وزن) — `D01F9D84-A1C8-46F3-A959-800DEF9E112F`
- Link instruments to existing normalized companies through `InsCode -> InstrumentCode`.
- Persist append-oriented history and a compact latest-quote projection.
- Resolve user-facing company/symbol quote requests through the canonical market-quote lookup path:
  - resolve the requested TSE symbol/company to `NoavaranEligibleCompanies.TseSymbol`
  - resolve `InstrumentCode` from `NoavaranEligibleCompanies`
  - resolve `TradingInstrumentId` from `TradingInstruments.InstrumentCode`
  - try `IntradayTradeSnapshots` for the current trading date first — **no hard `ProviderName` filter**
  - fall back to the latest `DailyInstrumentTrades` row when no intraday row exists for today — **no hard `ProviderName` filter**
  - `LatestMarketQuotes` may be used as a projection/cache fallback only if canonical table lookup fails — **no hard `ProviderName` filter** there either
  - the quote lookup must use the best available price record for the resolved instrument regardless of which provider populated it (`TsetmcWebService`, `StockMarketDb`, or any future source)
  - `ProviderName` is provenance metadata for audit/diagnostics and must not be used as a runtime quote eligibility filter
  - the API runtime `PrimarySourceName` (e.g. `StockMarketDb`) determines sync priority only; it must not cause the quote resolver to ignore valid canonical price records stored under a different `ProviderName`
  - return quote provenance/freshness, trading date, and daily change percentage from the selected row
  - `LATEST_PRICE` must equal `LastTradedPrice`
  - `DAILY_CHANGE_PCT` must equal `(LastTradedPrice / PriceYesterday - 1) * 100` using safe null-divide handling; do not calculate `DAILY_CHANGE_PCT` from `ClosingPrice`
  - user-facing `DAILY_CHANGE_PCT` must be formatted to two decimal places (e.g. `2.9842931937172800` → `2.98`)
- Add bounded, overlap-watermark polling workers with dataset-specific cadence.
- Support both **full-sync** (bounded historical backfill) and **incremental sync**
  (overlap-watermark forward sync) for every dataset above.

## Acceptance Criteria

1. Secrets remain external to source control and SQL access is read-only.
2. Trading instruments persist independently from companies and optionally link to an existing
   normalized company by TSETMC instrument code.
3. Intraday trades (`tse.Trade`), daily trades (`TSE.TradeRefined`), and intraday indices
   (`tse.IndexB1LastDay`) are idempotent on source identifiers.
4. Daily index rows persist from `Tse.IndexNew`, scoped by the named-index `InstrumentRef`
   values listed in Scope.
5. Every dataset supports a bounded full-sync (historical backfill) and an incremental
   overlap-watermark forward sync; the two paths share the same idempotent normalizers.
6. Polling uses bounded pages, overlap watermarks, retry/failure isolation, and telemetry.
7. `LatestMarketQuotes` exposes latest price, price-change percentage, source kind, and as-of
   timestamp with daily fallback when an intraday observation is unavailable.
8. Quote resolution for direct latest-price questions resolves `TradingInstrumentId` from
   `NoavaranEligibleCompanies.InstrumentCode -> TradingInstruments.InstrumentCode` and uses the
   same canonical quote path as valuation quote enrichment.
9. When an `IntradayTradeSnapshots` row exists for the current trading date (date-only
   comparison), the response uses that intraday quote and exposes latest price, daily change
   percentage, trading date, and source/freshness metadata.
10. When no intraday snapshot exists for the current trading date, the response falls back to
    the latest `DailyInstrumentTrades` row and does not show `Missing` if daily data exists.
11. Quote data is marked unavailable only when both today intraday and latest daily lookup fail.
12. Quote-backed responses expose the selected trading date in user-visible Persian/Jalali format
    while preserving canonical Gregorian storage for persistence/projections.
13. Valuation responses such as `PE` / `PS` quote enrichment use the same intraday-first,
    latest-daily fallback behavior for `LATEST_PRICE` and `DAILY_CHANGE_PCT`.
14. Monthly production/sales responses continue to suppress quote columns (`LATEST_PRICE`,
    `DAILY_CHANGE_PCT`, `آخرین قیمت`, `درصد تغییر آخرین قیمت`) even after quote fallback is
    corrected for direct price and valuation queries.
15. Scanner and market-summary caches invalidate after successful projection updates.
16. Unit, integration, architecture, and migration tests pass.
17. Runtime quote retrieval from canonical price tables (`IntradayTradeSnapshots`,
    `DailyInstrumentTrades`, `LatestMarketQuotes`) must **not** filter rows by `ProviderName`.
    The API runtime `PrimarySourceName` setting controls sync/projection-building priority only;
    it must not cause quote rows stored under a different `ProviderName` to be silently skipped.
18. `LATEST_PRICE` equals `LastTradedPrice`; `DAILY_CHANGE_PCT` equals
    `(LastTradedPrice / PriceYesterday - 1) * 100` with safe null-divide handling.
    `ClosingPrice` must not be used to derive `DAILY_CHANGE_PCT` in latest-price context.
    If closing-price change is ever needed it must be a separate metric (`CLOSING_PRICE_CHANGE_PCT`).
19. User-facing `DAILY_CHANGE_PCT` is formatted to two decimal places
    (e.g. raw `2.9842931937172800` displays as `+2.98%`).
20. Source/freshness label must reflect the actual selected quote path:
    `IntradayToday` when today's intraday row is used; `LatestDailyFallback` (or
    `PreviousTradingDay`) when the latest daily row is used; projection/cache label only when
    the projection path is actually used. A daily fallback row must not be mislabelled as intraday.
21. Given API `PrimarySourceName = StockMarketDb` and quote rows stored under
    `ProviderName = TsetmcWebService`, a user query for `آخرین قیمت شگل` must still return
    `LATEST_PRICE = 3934`, `DAILY_CHANGE_PCT = 2.98`, and no `Missing` cells.
    This provider-name mismatch scenario must be covered by a regression test.

## Out Of Scope

- Direct writes or DDL against `StockMarketDB`.
- Order-book depth, individual transaction tape, or portfolio valuation.
- Treating every registered instrument as a company.
- Replacing normalized PostgreSQL reads with query-time SQL Server access.
